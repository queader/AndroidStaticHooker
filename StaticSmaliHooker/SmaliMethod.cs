using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StaticSmaliHooker
{
    class SmaliMethod
    {
        public string OriginalSource { get; set; }
        public bool IsPatched { get; set; }

        public SmaliClass ParentClass { get; private set; }
        public string MethodName { get; private set; }
        public string ReturnType { get; private set; }
        public bool IsConstructor { get; private set; }
        public bool IsStatic { get; private set; }
        public List<SmaliAnnotation> Annotations { get; private set; }
        public List<string> Instructions { get; private set; }

        string prologueSource;

        public SmaliMethod(SmaliClass parentClass)
        {
            ParentClass = parentClass;
            Annotations = new List<SmaliAnnotation>();
            Instructions = new List<string>();
        }

        public override string ToString()
        {
            return string.Format("{0} : {1}", MethodName, ReturnType);
        }

        string GenerateRandomHookIndex()
        {
            return Program.Random.Next(0, 1000000).ToString();
        }

        public void InjectAfterHookCode(MethodToHook hook, int index)
        {
            Instructions.Insert(index++, "new-instance v0, Lcom/xquadplaystatic/MethodHookParam;");
            Instructions.Insert(index++, "invoke-direct {v0}, Lcom/xquadplaystatic/MethodHookParam;-><init>()V");

            //Instructions.Add("invoke-static {v1}, Lcom/xquadplaystatic/StaticHooks;->after_getSystemProperty(Lcom/xquadplaystatic/MethodHookParam;)V");
            Instructions.Insert(index++,
                string.Format("invoke-static {{v0}}, {0}->{1}(Lcom/xquadplaystatic/MethodHookParam;)V",
                hook.Interceptor.ParentClass.ClassName,
                hook.Interceptor.MethodName));

            Instructions.Insert(index++, "#hooked");
        }

        public void AddHookAfter(MethodToHook hook)
        {
            IsPatched = true;

            for (int n = 0; n < Instructions.Count; ++n)
            {
                if (Instructions[n].StartsWith("return-void") && !Instructions[n - 1].StartsWith("#hooked"))
                {
                    InjectAfterHookCode(hook, n);
                }
            }

            //string bottomHandlerID = ":hook_" + GenerateRandomHookIndex();

            //Instructions.Add("#");
            //Instructions.Add("####hook handler");
            //Instructions.Add("#");
            //Instructions.Add(bottomHandlerID);

            //Instructions.Add("new-instance v1, Lcom/xquadplaystatic/MethodHookParam;");
            //Instructions.Add("invoke-direct {v1}, Lcom/xquadplaystatic/MethodHookParam;-><init>()V");

            ////Instructions.Add("invoke-static {v1}, Lcom/xquadplaystatic/StaticHooks;->after_getSystemProperty(Lcom/xquadplaystatic/MethodHookParam;)V");
            //Instructions.Add(
            //    string.Format("invoke-static {{v1}}, {0}->{1}(Lcom/xquadplaystatic/MethodHookParam;)V",
            //    hook.Interceptor.ParentClass.ClassName,
            //    hook.Interceptor.MethodName));
        }

        public void PrintInstructions()
        {
            Console.WriteLine();

            foreach (var ins in Instructions)
            {
                Console.WriteLine(">   " + ins);
            }

            Console.WriteLine();
        }

        public string GetModifiedCode()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(prologueSource);

            foreach (var ins in Instructions)
            {
                sb.AppendLine("    " + ins);
            }

            sb.AppendLine(".end method");

            return sb.ToString();
        }

        public void Parse(string source)
        {
            OriginalSource = source;

            using (var reader = new StringReader(source))
            {
                StringBuilder prologueStringBuilder = new StringBuilder();

                string headerLine = reader.ReadLine();
                ParseHeaderLine(headerLine.Trim());

                prologueStringBuilder.AppendLine(headerLine);
                bool prologueFinished = false;

                string nextLine = null;
                while ((nextLine = reader.ReadLine()) != null)
                {
                    if (!prologueFinished)
                    {
                        prologueStringBuilder.AppendLine(nextLine);
                    }

                    if (string.IsNullOrEmpty(nextLine))
                        continue;

                    string trimmed = nextLine.Trim();

                    if (!prologueFinished)
                    {
                        if (trimmed.StartsWith(".annotation"))
                        {
                            string annotationBody = GetBodyToEndTag(reader, nextLine);
                            CreateAnnotation(annotationBody);
                        }

                        if (trimmed.StartsWith(".prologue"))
                        {
                            prologueFinished = true;
                        }
                    }
                    else
                    {
                        if (trimmed.StartsWith(".end method"))
                            continue;

                        Instructions.Add(trimmed);
                    }
                }

                prologueSource = prologueStringBuilder.ToString();
            }
        }

        void CreateAnnotation(string annotationBody)
        {
            var annot = new SmaliAnnotation();
            annot.Parse(annotationBody);

            if (annot.AnnotationType == "Lcom/xquadplaystatic/Hook;")
            {
                Program.RegisterMethodToHook(new MethodToHook(annot, this));
            }

            Annotations.Add(annot);
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

        void ParseHeaderLine(string header)
        {
            IsStatic = header.Contains(" static "); //whitespaces so we get no fake positives from method name
            IsConstructor = header.Contains(" constructor ");

            int firstParenthesis = header.IndexOf('(');

            int methodNameStart = firstParenthesis;
            while (header[methodNameStart - 1] != ' ')
                methodNameStart--;

            MethodName = header.Substring(methodNameStart, firstParenthesis - methodNameStart);

            int lastParenthesis = header.IndexOf(')');

            ReturnType = header.Substring(lastParenthesis + 1, header.Length - (lastParenthesis + 1));
        }
    }
}
