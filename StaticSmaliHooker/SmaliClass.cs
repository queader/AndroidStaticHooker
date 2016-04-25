using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StaticSmaliHooker
{
    class SmaliClass
    {
        public string SourcePath { get; set; }
        public string SourceCode { get; private set; }

        public string ClassName { get; private set; }
        public List<SmaliMethod> Methods { get; set; }
        public List<SmaliAnnotation> Annotations { get; set; }

        public SmaliClass()
        {
            Methods = new List<SmaliMethod>();
            Annotations = new List<SmaliAnnotation>();
        }

        public void CheckAndPatch()
        {
            string currentCode = SourceCode;
            bool modified = false;

            foreach (var method in Methods)
            {
                if (method.IsPatched)
                {
                    modified = true;
                    Console.WriteLine("      Patching Modified Method: {0}", method);

                    string modifiedCode = method.GetModifiedCode();
                    currentCode = currentCode.Replace(method.OriginalSource, modifiedCode);
                }
            }

            if (modified)
            {
                Console.WriteLine("      Saving Changes");
                File.WriteAllText(SourcePath, currentCode);
            }
        }

        public void Parse()
        {
            //Console.WriteLine("   Parsing Smali Class: {0}", SourcePath);
            SourceCode = File.ReadAllText(SourcePath);

            using (var reader = new StringReader(SourceCode))
            {
                ClassName = reader.ReadLine().Trim().Split(' ').Last();

                string nextLine = null;
                while ((nextLine = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrEmpty(nextLine))
                        continue;

                    string trimmed = nextLine.Trim();

                    if (trimmed.StartsWith(".method"))
                    {
                        string methodBody = GetBodyToEndTag(reader, nextLine);
                        CreateMethod(methodBody);
                    }
                }
            }

            //PrintInfo();
        }

        void PrintInfo()
        {
            Console.WriteLine("   Parsed Smali Class: {0}", ClassName);

            foreach (var method in Methods)
            {
                Console.WriteLine("      Method: {0} : {1}", method.MethodName, method.ReturnType);

                foreach (var annot in method.Annotations)
                {
                    Console.WriteLine("         Annot: {0}", annot.AnnotationType);

                    foreach (var prop in annot.Properties)
                    {
                        Console.WriteLine("            >: {0} = {1}", prop.Key, prop.Value);
                    }
                }
            }
        }

        void CreateMethod(string methodBody)
        {
            var method = new SmaliMethod(this);
            method.Parse(methodBody);

            Methods.Add(method);
        }

        static string GetBodyToEndTag(StringReader reader, string startingLine)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(startingLine);

            string trimmed = startingLine.Trim();
            string tag = trimmed.Split(' ')[0].Substring(1); //first part, skip the dot before
            string endindTag = ".end " + tag;

            string nextLine = null;
            while (true)
            {
                nextLine = reader.ReadLine();
                sb.AppendLine(nextLine);

                if (nextLine.Trim().StartsWith(endindTag))
                    break;
            }

            return sb.ToString();
        }
    }
}
