using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StaticSmaliHooker
{
    class SmaliAnnotation
    {
        public string OriginalSource { get; set; }

        public string AnnotationType { get; private set; }
        public Dictionary<string, string> Properties { get; private set; }

        public SmaliAnnotation()
        {
            Properties = new Dictionary<string, string>();
        }

        public void Parse(string source)
        {
            OriginalSource = source;

            using (var reader = new StringReader(source))
            {
                string headerLine = reader.ReadLine();
                ParseHeaderLine(headerLine.Trim());

                string nextLine = null;
                while ((nextLine = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrEmpty(nextLine))
                        continue;

                    string trimmed = nextLine.Trim();

                    if (trimmed.StartsWith(".end annotation"))
                        continue;

                    string[] split = trimmed.Split('=');
                    if (split.Length == 2)
                    {
                        string name = split[0].Trim();
                        string val = CleanPropertyVal(split[1].Trim());

                        Properties[name] = val;
                    }
                }
            }
        }

        static string CleanPropertyVal(string val)
        {
            if (val.StartsWith("\"") && val.EndsWith("\""))
            {
                return val.Substring(1, val.Length - 2);
            }
            return val;
        }

        void ParseHeaderLine(string header)
        {
            string[] split = header.Split(' ');

            AnnotationType = split[split.Length - 1];
        }
    }
}
