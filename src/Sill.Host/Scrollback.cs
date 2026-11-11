using System;
using System.Collections.Generic;
using System.Text;

namespace Sill.Host
{
    // 페이지에 보낸 출력과 그때 페이지의 크기(0.1.2 M1). 탭을 떼어 다시 붙은 새 페이지와 WebView2를 다시 만든 새 페이지에
    // 같은 크기로 다시 틀어 스크롤백을 되살린다. 같은 바이트를 같은 크기의 xterm.js에 넣으면 같은 화면이 나온다.
    // 잠그지 않는다. Program이 출력 보내기 잠금(Pending) 안에서만 부른다.
    sealed class Scrollback
    {
        readonly int _limit, _chunk;
        // "z<열>,<행>"(크기) 또는 "o<글>". 글은 _chunk 글자쯤씩 묶어 넣는다. 키 하나마다 항목을 만들면 항목 수가 글자 수만큼 는다.
        readonly LinkedList<string> _items = new LinkedList<string>();
        readonly StringBuilder _tail = new StringBuilder();     // 아직 묶지 않은 마지막 글
        int _length;            // 묶은 글의 글자 수(크기 항목은 세지 않음)
        string _base;           // 앞에서 버린 마지막 크기. 남은 첫 항목이 글이면 그 앞에 붙인다
        bool _cut;              // 앞에서 버린 글이 줄 중간에서 끝났다

        public Scrollback(int limit, int chunk) { _limit = limit; _chunk = chunk; }

        public void Add(string text)
        {
            _tail.Append(text);
            if (_tail.Length >= _chunk) Seal();
        }

        public void Size(string size)
        {
            Seal();
            _items.AddLast("z" + size);
        }

        void Seal()
        {
            if (_tail.Length == 0) return;
            _items.AddLast("o" + _tail);
            _length += _tail.Length;
            _tail.Clear();
            // 앞에서 버린다. 마지막 항목은 남긴다(하나가 상한보다 커도).
            while (_length > _limit && _items.Count > 1)
            {
                string first = _items.First.Value;
                _items.RemoveFirst();
                if (first[0] == 'z') _base = first;
                else { _length -= first.Length - 1; _cut = first[first.Length - 1] != '\n'; }
            }
        }

        // 다시 틀 메시지. 크기가 바뀐 곳마다 z, 그 사이의 글은 o 하나로 묶는다. 첫 항목은 크기이고,
        // 앞에서 줄 중간을 잘랐으면 첫 글은 첫 줄바꿈 뒤부터다(줄바꿈이 없으면 그대로).
        public List<string> Replay()
        {
            Seal();
            var list = new List<string>();
            var text = new StringBuilder();
            bool first = true;
            foreach (var item in _items)
            {
                if (item[0] == 'z')
                {
                    if (text.Length > 0) { list.Add("o" + text); text.Clear(); }
                    list.Add(item);
                    first = false;
                    continue;
                }
                if (first && _base != null) list.Add(_base);
                if (first && _cut)
                {
                    int nl = item.IndexOf('\n');
                    text.Append(nl < 0 ? item.Substring(1) : item.Substring(nl + 1));
                }
                else text.Append(item, 1, item.Length - 1);
                first = false;
            }
            if (text.Length > 0) list.Add("o" + text);
            return list;
        }
    }
}
