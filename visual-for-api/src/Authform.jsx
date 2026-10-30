import {useState} from 'react'

function Authform( { type } )
{
    return (
        type === 'login' ? (
            <div>
                <h1>Вход</h1>
                <form action="/login" method="POST">
                    <input type="text" placeholder="Логин" />
                    <input type="password" placeholder="Пароль" />
                    <button type="submit">Войти</button>
                </form>
            </div>
        ) : (
            <div>
                <h1>Регистрация</h1>
                <form action="/register" method="POST">
                    <input type="text" placeholder="Имя" />
                    <label>
                        <input type="radio" name="userType" value="admin" /> Админ
                        <input type="radio" name="userType" value="teamlead" /> Тимлид
                        <input type="radio" name="userType" value="developer" /> Разработчик
                    </label>
                    <input type="email" placeholder="Email" />
                    <input type="password" placeholder="Пароль" />
                    <button type="submit">Зарегистрироваться</button>
                </form>
            </div>
        )
    );
}
export default Authform;