import { useState } from 'react'
import Authform from './Authform'

function DashBord() 
{
    const [isLoggedIn, setIsLoggedIn] = useState('register')
    return (
        <>
            <div>
                Добро пожаловать в livecode
            </div>
            <button onClick={() => {setIsLoggedIn('login') (<Authform type={isLoggedIn}/>)}}>Войти</button>
            <button onClick={() => {setIsLoggedIn('link') (<Authform type={isLoggedIn}/>)}}>Ввести ссылку для входа</button>
            <button onClick={() => {setIsLoggedIn('register') (<Authform type={isLoggedIn}/>)}}>Регистрация</button>
        </>
    )

}

export default DashBord;