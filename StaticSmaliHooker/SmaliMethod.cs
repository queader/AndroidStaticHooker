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
        public int AllocatedRegisters { get; private set; }

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

        public void AddHookBefore(MethodToHook hook)
        {
            IsPatched = true;

            int index = 0;

            Instructions.Insert(index++, "new-instance v0, Lcom/xquadplaystatic/MethodHookParam;");
            Instructions.Insert(index++, "invoke-direct {v0}, Lcom/xquadplaystatic/MethodHookParam;-><init>()V");

            Instructions.Insert(index++,
                string.Format("invoke-static {{v0}}, {0}->{1}(Lcom/xquadplaystatic/MethodHookParam;)V",
                hook.Interceptor.ParentClass.ClassName,
                hook.Interceptor.MethodName));

            Instructions.Insert(index++, "iget-boolean v2, v1, Lcom/xquadplaystatic/MethodHookParam;->returnEarly:Z");
            Instructions.Insert(index++, "if-eqz v2, :cond_normal_run");

            //if we canceled executing method by calling setResult()
            if (ReturnType == "V")
            {
                Instructions.Insert(index++, "return-void");
            }
            else
            {
                Instructions.Insert(index++, "invoke-virtual {v0}, Lcom/xquadplaystatic/MethodHookParam;->getResult()Ljava/lang/Object;");
                Instructions.Insert(index++, "move-result-object v1");
                Instructions.Insert(index++, "return-object v1");
            }

            Instructions.Insert(index++, ":cond_normal_run");
        }

        public void InjectAfterHookCode(MethodToHook hook, ref int index, bool hasReturnValue)
        {
            Instructions.Insert(index++, "new-instance v0, Lcom/xquadplaystatic/MethodHookParam;");
            Instructions.Insert(index++, "invoke-direct {v0}, Lcom/xquadplaystatic/MethodHookParam;-><init>()V");

            if (hasReturnValue)
            {
                Instructions.Insert(index++, "invoke-virtual {v0, v1}, Lcom/xquadplaystatic/MethodHookParam;->setResult(Ljava/lang/Object;)V");
            }

            //Instructions.Add("invoke-static {v1}, Lcom/xquadplaystatic/StaticHooks;->after_getSystemProperty(Lcom/xquadplaystatic/MethodHookParam;)V");
            Instructions.Insert(index++,
                string.Format("invoke-static {{v0}}, {0}->{1}(Lcom/xquadplaystatic/MethodHookParam;)V",
                hook.Interceptor.ParentClass.ClassName,
                hook.Interceptor.MethodName));

            if (hasReturnValue)
            {
                Instructions.Insert(index++, "invoke-virtual {v0}, Lcom/xquadplaystatic/MethodHookParam;->getResult()Ljava/lang/Object;");
                Instructions.Insert(index++, "move-result-object v1");
                Instructions.Insert(index++, string.Format("check-cast v1, {0}", ReturnType));
            }

            //Instructions.Insert(index++, "#hooked");
        }

        public void AddHookAfter(MethodToHook hook)
        {
            IsPatched = true;

            for (int n = 0; n < Instructions.Count; ++n)
            {
                if (n >= 1 && Instructions[n - 1].StartsWith("#hooked"))
                    continue;

                string instr = Instructions[n];

                if (instr.StartsWith("return-void"))
                {
                    int index = n;
                    InjectAfterHookCode(hook, ref index, false);

                    Instructions.Insert(index++, "#hooked");
                }
                if (instr.StartsWith("return "))
                {
                    int index = n;
                    string returnValRegister = instr.Split(' ')[1];

                    if (returnValRegister != "v1")
                    {
                        Instructions.Insert(index++, string.Format("move v1, {0}", returnValRegister));
                    }

                    InjectAfterHookCode(hook, ref index, false);

                    if (returnValRegister != "v1")
                    {
                        Instructions.Insert(index++, string.Format("move {0}, v1", returnValRegister));
                    }

                    Instructions.Insert(index++, "#hooked");
                }
                if (instr.StartsWith("return-object"))
                {
                    int index = n;
                    string returnValRegister = instr.Split(' ')[1];

                    if (returnValRegister != "v1")
                    {
                        Instructions.Insert(index++, string.Format("move-object v1, {0}", returnValRegister));
                    }

                    InjectAfterHookCode(hook, ref index, true);

                    if (returnValRegister != "v1")
                    {
                        Instructions.Insert(index++, string.Format("move-object {0}, v1", returnValRegister));
                    }

                    Instructions.Insert(index++, "#hooked");
                }
            }
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
