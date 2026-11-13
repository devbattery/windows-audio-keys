// Windows Audio Keys - 키 녹화 입력을 단축키 조합으로 바꾸는 순수 로직(화면 없음).
// 설정 창의 KeyRecorder가 사용하며, 콘솔 테스트에서도 그대로 검증한다.

using System.Windows.Forms;

namespace GameAudioKeys
{
    internal enum KeyCaptureKind
    {
        // 수정자(Ctrl/Alt/Shift)만 눌린 상태: 일반 키를 더 기다린다.
        Pending,
        // 사용할 수 있는 조합이 완성됨
        Complete,
        // 지원하지 않거나 예약된 조합: 안내 후 계속 녹화한다.
        Rejected
    }

    internal sealed class KeyCaptureResult
    {
        public KeyCaptureKind Kind;
        public HotkeyBinding Binding;
        public string MessageKo;
    }

    internal static class KeyCapture
    {
        // keyData는 WinForms 키 데이터(키 코드 | Control/Alt/Shift 수정자 비트)이다.
        public static KeyCaptureResult Evaluate(Keys keyData)
        {
            KeyCaptureResult result = new KeyCaptureResult();
            Keys key = keyData & Keys.KeyCode;
            bool ctrl = (keyData & Keys.Control) == Keys.Control;
            bool alt = (keyData & Keys.Alt) == Keys.Alt;
            bool shift = (keyData & Keys.Shift) == Keys.Shift;

            if (IsModifierKey(key))
            {
                result.Kind = KeyCaptureKind.Pending;
                return result;
            }

            result.Kind = KeyCaptureKind.Rejected;
            if (key == Keys.LWin || key == Keys.RWin)
            {
                result.MessageKo = "Windows 키가 포함된 조합은 사용할 수 없습니다.";
                return result;
            }

            HotkeyBinding binding = new HotkeyBinding((int)key, ctrl, alt, shift);

            // 예약 조합(Ctrl+Alt+Delete, Alt+F4, F12 등)은 저장 시 검증과 같은 문구로 안내한다.
            string problem = SettingsValidator.DescribeBindingProblem(binding);
            if (problem != null)
            {
                // 예약 조합·F12는 검증기의 구체적인 이유를, 단순히 목록에 없는 키는
                // 내부 키 번호 대신 쓸 수 있는 키 안내를 보여 준다.
                bool merelyUnsupported = !KeyCatalog.IsSupported((int)key)
                    && problem.IndexOf("지원하지 않는 키", System.StringComparison.Ordinal) >= 0;
                result.MessageKo = merelyUnsupported
                    ? "이 키는 지원하지 않습니다. Delete, Page Up/Down, Insert, Home, End, 방향키, F1~F11, 영문, 숫자를 사용하세요."
                    : problem;
                return result;
            }

            result.Kind = KeyCaptureKind.Complete;
            result.Binding = binding;
            return result;
        }

        // 녹화 중 진행 표시용: 현재 눌린 수정자를 "Ctrl + Alt + …" 형태로 만든다.
        public static string DescribePending(Keys modifiers)
        {
            string text = string.Empty;
            if ((modifiers & Keys.Control) == Keys.Control)
            {
                text += "Ctrl + ";
            }

            if ((modifiers & Keys.Alt) == Keys.Alt)
            {
                text += "Alt + ";
            }

            if ((modifiers & Keys.Shift) == Keys.Shift)
            {
                text += "Shift + ";
            }

            return text.Length == 0 ? null : text + "…";
        }

        private static bool IsModifierKey(Keys key)
        {
            return key == Keys.ControlKey || key == Keys.LControlKey || key == Keys.RControlKey
                || key == Keys.ShiftKey || key == Keys.LShiftKey || key == Keys.RShiftKey
                || key == Keys.Menu || key == Keys.LMenu || key == Keys.RMenu;
        }
    }
}
