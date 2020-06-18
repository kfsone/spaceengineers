using System;
using System.Collections.Generic;

namespace IngameScript
{
    class Config
    {
        public Dictionary<string, string> Values { get; } = new Dictionary<string, string>();

        public Config(string data) { Parse(data); }

        private bool ParseLine(string line, out string key, out string value)
        {
            var comment = line.IndexOf("#");
            if (comment != -1)
                line = line.Substring(0, comment);
            line = line.Trim();
            if (line == string.Empty)
            {
                key = value = "";
                return false;
            }

            // If the format isn't xxx=yyy, then we treat this as a positive boolean,
            // which can be negated with a '!'.
            //   foo    :-    foo=true
            //   !foo   :-    foo=false
            var equals = line.IndexOf("=");
            if (equals == -1)
            {
                bool negate = line.StartsWith("!");
                key = negate ? line.Substring(1) : line;
                value = negate ? "false" : "true";
            }
            else
            {
                key = line.Substring(0, equals).Trim();
                value = line.Substring(equals + 1).Trim();
            }

            return true;
        }

        public void Parse(string data)
        {
            string key, value;
            foreach (var line in data.Split('\n'))
            {
                if (!ParseLine(line, out key, out value))
                    continue;
                Values[key] = value;
            }
        }

        public string this[string key]
        {
            get
            {
                string value;
                return Values.TryGetValue(key, out value) ? value : "";
            }
        }

        public bool AsBool(string key) => bool.Parse(this[key]);

        public float AsFloat(string key) => float.Parse(this[key]);

        public int AsInt(string key) => int.Parse(this[key]);

        public string AsString(string key) => this[key];
    }
}
