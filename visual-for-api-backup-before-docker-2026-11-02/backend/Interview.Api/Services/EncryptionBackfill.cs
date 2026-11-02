using Interview.Api.Data;
using Interview.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Interview.Api.Services;

// Выполняется оператором после резервного копирования БД и применения миграции.
public static class EncryptionBackfill
{
    public static async Task Run(AppDbContext db)
    {
        var fields = new Dictionary<Type, string[]>
        {
            [typeof(BankTask)] = ["StarterCode"],
            [typeof(TaskTest)] = ["Input", "Expected"],
            [typeof(Room)] = ["RubricSnapshot"],
            [typeof(RoomTask)] = ["TaskSnapshot"],
            [typeof(CodeDocument)] = ["Code"],
            [typeof(CodeEvent)] = ["Code"],
            [typeof(Submission)] = ["Code"],
            [typeof(CodeRun)] = ["Code", "Output"],
            [typeof(ChatMessage)] = ["Text"],
            [typeof(Hint)] = ["Text"],
            [typeof(Evaluation)] = ["Comment"],
            [typeof(SessionEvent)] = ["Payload"]
        };
        foreach (var (type, names) in fields)
        {
            var method = typeof(EncryptionBackfill).GetMethod(nameof(EncryptTable), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.MakeGenericMethod(type);
            await (Task)method.Invoke(null, [db, names])!;
        }
    }

    private static async Task EncryptTable<T>(AppDbContext db, string[] names) where T : class
    {
        // Потоковое чтение не загружает всю таблицу целиком. Промежуточные строки остаются зашифрованными при повторном запуске.
        var count = await db.Set<T>().CountAsync();
        for (var offset = 0; offset < count; offset += 100)
        {
            var keyNames = db.Model.FindEntityType(typeof(T))!.FindPrimaryKey()!.Properties.Select(p => p.Name).ToArray();
            IQueryable<T> query = db.Set<T>();
            // Упорядочивание по ключу задаём через EF.Property, включая составные ключи.
            IOrderedQueryable<T>? ordered = null;
            foreach (var key in keyNames) ordered = ordered == null ? query.OrderBy(x => EF.Property<object>(x, key)) : ordered.ThenBy(x => EF.Property<object>(x, key));
            var rows = await ordered!.Skip(offset).Take(100).ToListAsync();
            foreach (var row in rows) foreach (var name in names) db.Entry(row).Property(name).IsModified = true;
            await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        }
    }
}
