using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Text;

// 버전은 Shared.Version 하나다. exe·DLL의 파일·제품 버전이 되고, 설치 스크립트(sill.iss)가 sill-host.exe에서 읽는다.
// AssemblyVersion은 0.0.0.0으로 둔다. 등록 키의 Assembly 값(register.ps1, sill.iss)이 그 버전을 적고 있다.
[assembly: System.Reflection.AssemblyFileVersion(Sill.Shared.Version + ".0")]
[assembly: System.Reflection.AssemblyInformationalVersion(Sill.Shared.Version)]

namespace Sill
{
    // band와 sill-host가 함께 쓰는 코드. 파이프는 메시지 모드라서 Write 한 번이 메시지 하나로 도착한다.
    // 메시지는 UTF-8 문자열이고 첫 글자가 종류다: r(크기) i(입력) o(출력) c(탐색기 폴더) p(셸 폴더). M1·M2·M3.
    // sill-host → band: e(셸의 종료 코드, 0이면 band가 탭을 닫음). M9.
    // 페이지와 band 사이에만 오가는 것: s(설정 파일) x(끊김) n(다시 시작) h(패널 닫기) t(앱 모드) l(페이지가 떴다) +(새 터미널) a(활성) -(닫기) u(링크 열기).
    // 페이지와 band 사이에서는 한 터미널의 메시지에 번호가 붙는다(i2 dir). M6, M7, M9.
    static class Shared
    {
        public const string Version = "0.1.1";

        [ThreadStatic] static byte[] _buf;      // 읽는 스레드마다 하나. 메시지마다 64 KB를 새로 만들지 않는다(M10)

        public static string Read(PipeStream pipe)
        {
            var buf = _buf ?? (_buf = new byte[65536]);
            int n = pipe.Read(buf, 0, buf.Length);
            if (n == 0) return null;
            if (pipe.IsMessageComplete) return Encoding.UTF8.GetString(buf, 0, n);
            var ms = new MemoryStream();
            ms.Write(buf, 0, n);
            do
            {
                n = pipe.Read(buf, 0, buf.Length);
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

        static readonly string LogDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sill");
        static readonly int Pid = Process.GetCurrentProcess().Id;
        static bool _logDirMade;

        // %LOCALAPPDATA%\Sill\<file>에 "시각 [PID] 내용" 한 줄. 여러 프로세스(탐색기 창들, sill-host들)가 같은 파일에 쓴다.
        // 덧붙이기 전용으로 열고 서로 쓰기를 허락해야 공유 위반으로 줄이 사라지지 않는다(M10).
        // 1 MB를 넘으면 <file>.old로 옮긴다(M5). FileShare.Delete라 다른 프로세스가 쓰는 중에도 옮겨진다.
        // ponytail: 두 프로세스가 동시에 옮기면 늦은 쪽이 방금 옮긴 .old를 지울 수 있다. 로그라서 그대로 둔다.
        public static void Log(string file, string format, params object[] args)
        {
#if !SILL_TEST     // 단위 시험은 사용자의 로그에 쓰지 않는다
            try
            {
                if (!_logDirMade) { Directory.CreateDirectory(LogDir); _logDirMade = true; }
                var path = Path.Combine(LogDir, file);
                var b = Encoding.UTF8.GetBytes(string.Format("{0:HH:mm:ss.fff} [{1}] {2}\r\n", DateTime.Now, Pid, string.Format(format, args)));
                long length;
                using (var f = new FileStream(path, FileMode.Append, FileSystemRights.AppendData,
                    FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.None))
                {
                    f.Write(b, 0, b.Length);
                    length = f.Length;
                }
                if (length > 1 << 20)
                {
                    File.Delete(path + ".old");
                    File.Move(path, path + ".old");
                }
            }
            catch { }
#endif
        }
    }
}
