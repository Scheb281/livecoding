using System.Net;
using System.Net.Http.Json;
using Interview.Runner;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Interview.Api.Tests;

public sealed class DockerTheoryAttribute : TheoryAttribute
{
    public DockerTheoryAttribute() { if (Environment.GetEnvironmentVariable("INTERVIEW_TEST_DOCKER") != "1") Skip = "Включите INTERVIEW_TEST_DOCKER=1 после сборки sandbox-образа."; }
}
public sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute() { if (Environment.GetEnvironmentVariable("INTERVIEW_TEST_DOCKER") != "1") Skip = "Включите INTERVIEW_TEST_DOCKER=1 после сборки sandbox-образа."; }
}
public sealed class RunnerFactory : WebApplicationFactory<RunnerProgram>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Runner:ApiKey"] = "synthetic-test-key-not-for-production-123456" }));
}
public class DockerTests
{
    private static DockerSandbox Sandbox() => new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Runner:SandboxImage"] = "visual-interview-sandbox:local" }).Build());

    [Fact]
    public async Task Runner_Rejects_Unauthenticated_Requests_And_Invalid_Jobs()
    {
        await using var factory = new RunnerFactory(); using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/execute", new { })).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "synthetic-test-key-not-for-production-123456");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/execute", new SandboxJob(Guid.NewGuid(), "bash", "", 1000, 128, [new(0, "")]))).StatusCode);
    }

    [Fact]
    public void Container_Policy_Prohibits_Network_Host_Mounts_And_Privileges()
    {
        var args = DockerSandbox.CreateArguments("interview-test", "image", 128);
        Assert.Contains("none", args); Assert.Contains("--read-only", args); Assert.Contains("--cap-drop", args);
        Assert.Contains("no-new-privileges:true", args); Assert.Contains("65532:65532", args);
        Assert.DoesNotContain("--privileged", args); Assert.DoesNotContain("--volume", args); Assert.DoesNotContain("--mount", args);
    }

    [DockerTheory]
    [InlineData("Python", "print(5)")]
    [InlineData("JavaScript", "console.log(5)")]
    [InlineData("TypeScript", "const value: number = 5; console.log(value)")]
    [InlineData("C++", "#include <iostream>\nint main(){std::cout<<5<<std::endl;}")]
    [InlineData("C#", "using System; class Program { static void Main(){Console.WriteLine(5);} }")]
    [InlineData("Go", "package main\nimport \"fmt\"\nfunc main(){fmt.Println(5)}")]
    [InlineData("Java", "public class Main { public static void main(String[] args){System.out.println(5);} }")]
    public async Task Languages_Run_Inside_Real_Containers(string language, string code)
    {
        var result = await Sandbox().Execute(new(Guid.NewGuid(), language, code, 3000, 256, [new(0, "")]), CancellationToken.None);
        Assert.True(result.Tests[0].ErrorKind == null, result.Tests[0].Output + " / " + result.Tests[0].ErrorKind);
        Assert.Equal("5", result.Tests[0].Output.Trim());
    }

    [DockerTheory]
    [InlineData("while True: pass", "timeout")]
    [InlineData("while True: print('x'*4096)", "output")]
    [InlineData("def broken(:", "runtime")]
    public async Task Time_And_Output_And_Runtime_Errors_Are_Bounded(string code, string expected)
    {
        var result = await Sandbox().Execute(new(Guid.NewGuid(), "Python", code, 500, 128, [new(0, "")]), CancellationToken.None);
        Assert.Equal(expected, result.Tests[0].ErrorKind);
        Assert.True(result.Tests[0].Output.Length <= 32768);
    }

    [DockerFact]
    public async Task Compilation_Errors_And_Network_Isolation_Are_Reported()
    {
        var invalid = await Sandbox().Execute(new(Guid.NewGuid(), "C++", "int main( {", 1000, 128, [new(0, "")]), CancellationToken.None);
        Assert.Equal("compile", invalid.Tests[0].ErrorKind);
        var code = "import os,socket\ns=socket.socket()\ntry:\n s.connect(('1.1.1.1',80)); print('BAD')\nexcept OSError:\n print(str(os.getuid())+':'+str(os.path.exists('/var/run/docker.sock')))";
        var isolated = await Sandbox().Execute(new(Guid.NewGuid(), "Python", code, 1000, 128, [new(0, "")]), CancellationToken.None);
        Assert.Equal("65532:False", isolated.Tests[0].Output.Trim());
    }

    [DockerFact]
    public async Task Memory_Is_Bounded_And_Test_Files_Do_Not_Leak_Between_Tests()
    {
        var memory = await Sandbox().Execute(new(Guid.NewGuid(), "Python", "data=bytearray(1024*1024*1024)", 1000, 64, [new(0, "")]), CancellationToken.None);
        Assert.NotNull(memory.Tests[0].ErrorKind);
        var code = "from pathlib import Path\np=Path('seen')\nif p.exists():\n print(p.read_text())\nelse:\n p.write_text(input()); print('fresh')";
        var isolation = await Sandbox().Execute(new(Guid.NewGuid(), "Python", code, 1000, 128, [new(0, "PRIVATE_INPUT"), new(1, "public")]), CancellationToken.None);
        Assert.All(isolation.Tests, test => Assert.Equal("fresh", test.Output.Trim()));
        var readonlyRoot = await Sandbox().Execute(new(Guid.NewGuid(), "Python", "import os\nprint(bool(os.statvfs('/').f_flag & os.ST_RDONLY))", 1000, 128, [new(0, "")]), CancellationToken.None);
        Assert.Equal("True", readonlyRoot.Tests[0].Output.Trim());
    }

    [DockerFact]
    public async Task Authenticated_Http_Endpoint_Executes_Only_In_Sandbox()
    {
        await using var factory = new RunnerFactory(); using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "synthetic-test-key-not-for-production-123456");
        var response = await client.PostAsJsonAsync("/execute", new SandboxJob(Guid.NewGuid(), "Python", "print(5)", 1000, 128, [new(0, "")]));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<SandboxResult>(); Assert.Equal("5", result!.Tests[0].Output.Trim());
    }

    [DockerFact]
    public async Task TypeScript_Can_Read_Test_Input_Through_Node_Standard_Library()
    {
        var code = "import { readFileSync } from 'node:fs'; const values = readFileSync(0, 'utf8').trim().split(' ').map(Number); console.log(values[0] + values[1]);";
        var result = await Sandbox().Execute(new(Guid.NewGuid(), "TypeScript", code, 2000, 128, [new(0, "2 3")]), CancellationToken.None);
        Assert.True(result.Tests[0].ErrorKind == null, result.Tests[0].Output);
        Assert.Equal("5", result.Tests[0].Output.Trim());
    }
}
