using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace Sill
{
    // band와 sill-host가 함께 쓰는 코드. 파이프는 메시지 모드라서 Write 한 번이 메시지 하나로 도착한다.
    // 메시지는 UTF-8 문자열이고 첫 글자가 종류다: r(크기) i(입력) o(출력) c(탐색기 폴더). M1 설계 표 2, M2 설계 표 2.
    static class Shared
    {
        public static string Read(PipeStream pipe)
        {
            var ms = new MemoryStream();
            var buf = new byte[65536];
            do
            {
                int n = pipe.Read(buf, 0, buf.Length);
                if (n == 0) return null;
                ms.Write(buf, 0, n);
            } while (!pipe.IsMessageComplete);
            return Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
        }

        public static void Write(PipeStream pipe, string message)
        {
            var b = Encoding.UTF8.GetBytes(message);
            lock (pipe) pipe.Write(b, 0, b.Length);
        }

        // %LOCALAPPDATA%\Sill\<file>에 "시각 [PID] 내용" 한 줄
        public static void Log(string file, string format, params object[] args)
        {
            try
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sill", file);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.AppendAllText(path, string.Format("{0:HH:mm:ss.fff} [{1}] {2}\r\n",
                    DateTime.Now, Process.GetCurrentProcess().Id, string.Format(format, args)));
            }
            catch { }
        }
    }
}
