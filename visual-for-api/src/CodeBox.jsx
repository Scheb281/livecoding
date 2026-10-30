import { useRef, useState } from "react";
import Editor from "@monaco-editor/react";

function CodeBox() {
  const languagesConfig = {
    Python: {
      id: "python",
      filename: "main.py",
      pistonName: "python",
      version: "3.10.0",
      starterCode: `import sys


def main():
    # Напишите решение здесь
    print("Hello, World!", file=sys.stdout)


if __name__ == "__main__":
    main()
`,
    },
    JavaScript: {
      id: "javascript",
      filename: "index.js",
      pistonName: "javascript",
      version: "18.15.0",
      starterCode: `function main() {
    // Напишите решение здесь
    console.log("Hello, World!");
}

main();
`,
    },
    TypeScript: {
      id: "typescript",
      filename: "index.ts",
      pistonName: "typescript",
      version: "4.9.5",
      starterCode: `function main(): void {
    // Напишите решение здесь
    console.log("Hello, World!");
}

main();
`,
    },
    "C++": {
      id: "cpp",
      filename: "main.cpp",
      pistonName: "c++",
      version: "10.2.0",
      starterCode: `#include <iostream>

int main() {
    // Напишите решение здесь
    std::cout << "Hello, World!" << std::endl;
    return 0;
}
`,
    },
    "C#": {
      id: "csharp",
      filename: "Program.cs",
      pistonName: "csharp",
      version: "6.12.0",
      starterCode: `using System;

class Program
{
    static void Main(string[] args)
    {
        // Напишите решение здесь
        Console.WriteLine("Hello, World!");
    }
}
`,
    },
    Go: {
      id: "go",
      filename: "main.go",
      pistonName: "go",
      version: "1.16.2",
      starterCode: `package main

import "fmt"

func main() {
    // Напишите решение здесь
    fmt.Println("Hello, World!")
}
`,
    },
    Java: {
      id: "java",
      filename: "Main.java",
      pistonName: "java",
      version: "15.0.2",
      starterCode: `import java.util.*;

public class Main {
    public static void main(String[] args) {
        // Напишите решение здесь
        System.out.println("Hello, World!");
    }
}
`,
    },
  };

  const [isOpen, setIsOpen] = useState(false);
  const [selectedLang, setSelectedLang] = useState("Python");

  const [output, setOutput] = useState(
    "Здесь будет результат выполнения кода...",
  );
  const [isLoading, setIsLoading] = useState(false);

  const editorRef = useRef(null);

  const handleEditorDidMount = (editor) => {
    editorRef.current = editor;
  };

  const toggleDropdown = () => {
    setIsOpen((prev) => !prev);
  };
  const runCode = async () => {
    if (!editorRef.current) return;

    const currentCode = editorRef.current.getValue();

    setIsLoading(true);
    setOutput("Выполнение кода...");

    const langInfo = languagesConfig[selectedLang];
    const executeUrl = "/api/code/execute";

    try {
      const response = await fetch(executeUrl, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
        },
        body: JSON.stringify({
          language: langInfo.pistonName,
          version: langInfo.version,
          files: [
            {
              name: langInfo.filename,
              content: currentCode,
            },
          ],
        }),
      });

      const data = await response.json().catch(() => null);

      if (!response.ok) {
        if (response.status === 404 || response.status === 501) {
          throw new Error(
            "Сервер выполнения кода пока не подключён. " +
              "Нужен обработчик POST /api/code/execute на бэкенде.",
          );
        }

        if (response.status === 401 || response.status === 403) {
          throw new Error(
            "Сервер не разрешил выполнение кода. Проверьте вход и доступ к сервису выполнения.",
          );
        }

        throw new Error(
          data?.message || `Сервер выполнения ответил HTTP ${response.status}.`,
        );
      }

      // Ошибки компиляции приходят отдельно от результата запуска программы.
      if (data?.compile && (data.compile.code !== 0 || data.compile.signal)) {
        throw new Error(data.compile.output || "Ошибка компиляции программы.");
      }

      if (!data?.run) {
        throw new Error(
          "Маршрут POST /api/code/execute не вернул результат. " +
            "Проверьте подключение сервера выполнения.",
        );
      }

      const runFailed = data.run.code !== 0 || data.run.signal;
      setOutput(
        data.run.output ||
          (runFailed
            ? "Программа завершилась с ошибкой без вывода."
            : "Код успешно выполнился, но ничего не вывел."),
      );
    } catch (error) {
      setOutput(
        error instanceof TypeError
          ? "Не удалось подключиться к серверу выполнения. Проверьте его адрес и доступность."
          : `Ошибка: ${error.message}`,
      );
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <div
      style={{
        padding: "20px",
        fontFamily: "sans-serif",
        backgroundColor: "#1e1e1e",
        color: "#fff",
      }}
    >
      <div className="code-box__toolbar">
        <div className="dropdown" style={{ position: "relative" }}>
          <button
            onClick={toggleDropdown}
            style={{
              padding: "10px 15px",
              cursor: "pointer",
              background: "#333",
              color: "#fff",
              border: "1px solid #555",
              borderRadius: "4px",
            }}
          >
            <strong>{selectedLang}</strong> {isOpen ? "▲" : "▼"}
          </button>

          {isOpen && (
            <ul
              style={{
                position: "absolute",
                top: "100%",
                left: 0,
                margin: "4px 0 0 0",
                padding: "8px 0",
                listStyle: "none",
                border: "1px solid #444",
                background: "#252526",
                borderRadius: "4px",
                zIndex: 10,
              }}
            >
              {Object.keys(languagesConfig).map((language) => (
                <li
                  key={language}
                  onClick={() => {
                    setSelectedLang(language);
                    setIsOpen(false);
                  }}
                  style={{
                    padding: "6px 20px",
                    cursor: "pointer",
                    backgroundColor:
                      selectedLang === language ? "#37373d" : "transparent",
                  }}
                >
                  {language}
                </li>
              ))}
            </ul>
          )}
        </div>

        <button
          onClick={runCode}
          disabled={isLoading}
          style={{
            padding: "10px 25px",
            cursor: isLoading ? "not-allowed" : "pointer",
            background: "#4CAF50",
            color: "#fff",
            border: "none",
            borderRadius: "4px",
            fontWeight: "bold",
            opacity: isLoading ? 0.6 : 1,
          }}
        >
          {isLoading ? "Запуск..." : "▶ Запустить код"}
        </button>
      </div>

      <div
        style={{
          border: "1px solid #444",
          borderRadius: "4px",
          overflow: "hidden",
          marginBottom: "20px",
        }}
      >
        <Editor
          height="50vh"
          theme="vs-dark"
          language={languagesConfig[selectedLang].id}
          path={languagesConfig[selectedLang].filename}
          defaultValue={languagesConfig[selectedLang].starterCode}
          onMount={handleEditorDidMount}
          options={{
            fontSize: 14,
            minimap: { enabled: false },
            automaticLayout: true,
          }}
        />
      </div>

      <div>
        <div
          style={{
            fontSize: "14px",
            marginBottom: "5px",
            color: "#aaa",
            fontWeight: "bold",
          }}
        >
          Консоль вывода:
        </div>
        <pre
          style={{
            margin: 0,
            padding: "15px",
            backgroundColor: "#000",
            color: "#00ff00",
            fontFamily: "monospace",
            fontSize: "14px",
            borderRadius: "4px",
            border: "1px solid #333",
            minHeight: "100px",
            whiteSpace: "pre-wrap",
            overflowY: "auto",
          }}
        >
          {output}
        </pre>
      </div>
    </div>
  );
}

export default CodeBox;
