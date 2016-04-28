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
        public List<string> ParameterTypes { get; private set; }
        public int OriginalAllocatedRegisters { get; private set; }
        public int AllocatedRegisters { get; private set; }
        public string RawParameterLine { get; private set; }

        string prologueSource;

        public SmaliMethod(SmaliClass parentClass)
        {
            ParentClass = parentClass;
            Annotations = new List<SmaliAnnotation>();
            Instructions = new List<string>();
            ParameterTypes = new List<string>();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();

            if (ParameterTypes.Count > 0)
            {
                foreach (var el in ParameterTypes)
                {
                    sb.Append(el);
                    sb.Append(", ");
                }
                sb.Length = sb.Length - 2;
            }

            return string.Format("{0} ({1}) : {2}", MethodName, sb.ToString(), ReturnType);
        }

        string GenerateRandomHookIndex()
        {
            return Program.Random.Next(0, 1000000).ToString();
        }

        string GetMirroredParameterRegister(int n)
        {
            return "v" + ((OriginalAllocatedRegisters - ParameterTypes.Count) + 3 + n);
        }

        void AddParameterMirroringCode(ref int index)
        {
            if (IsStatic)
            {
                for (int n = 0; n < ParameterTypes.Count; ++n)
                {
                    string currParamRegister = "p" + n;

                    Instructions.Insert(index++,
                        string.Format("move-object {0}, {1}", GetMirroredParameterRegister(n), currParamRegister));
                }
            }
            else
            {
                Instructions.Insert(index++,
                        string.Format("move-object {0}, p0", GetMirroredParameterRegister(0)));

                for (int n = 1; n <= ParameterTypes.Count; ++n)
                {
                    string currParamRegister = "p" + n;

                    Instructions.Insert(index++,
                        string.Format("move-object {0}, {1}", GetMirroredParameterRegister(n), currParamRegister));
                }
            }
        }

        public void PackPrimitiveValue(ref int index, string primitiveType, string register)
        {
            switch (primitiveType)
            {
                case "I":
                    {
                        Instructions.Insert(index++,
                            string.Format("invoke-static {{{0}}}, Ljava/lang/Integer;->valueOf(I)Ljava/lang/Integer;", register));
                        Instructions.Insert(index++,
                            string.Format("move-result-object {0}", register));

                        return;
                    }
                case "Z":
                    {
                        Instructions.Insert(index++,
                            string.Format("invoke-static {{{0}}}, Ljava/lang/Boolean;->valueOf(Z)Ljava/lang/Boolean;", register));
                        Instructions.Insert(index++,
                            string.Format("move-result-object {0}", register));

                        return;
                    }
                case "B":
                    {
                        Instructions.Insert(index++,
                            string.Format("invoke-static {{{0}}}, Ljava/lang/Byte;->valueOf(B)Ljava/lang/Byte;", register));
                        Instructions.Insert(index++,
                            string.Format("move-result-object {0}", register));

                        return;
                    }
            }

            throw new Exception("Unsupported primitive value: " + primitiveType);
        }

        public void UnpackPrimitiveValue(ref int index, string primitiveType, string register)
        {
            switch (primitiveType)
            {
                case "I":
                    {
                        Instructions.Insert(index++,
                            string.Format("check-cast {0}, Ljava/lang/Integer;", register));
                        Instructions.Insert(index++,
                            string.Format("invoke-virtual {{{0}}}, Ljava/lang/Integer;->intValue()I", register));
                        Instructions.Insert(index++,
                            string.Format("move-result {0}", register));

                        return;
                    }
                case "Z":
                    {
                        Instructions.Insert(index++,
                            string.Format("check-cast {0}, Ljava/lang/Boolean;", register));
                        Instructions.Insert(index++,
                            string.Format("invoke-virtual {{{0}}}, Ljava/lang/Boolean;->booleanValue()Z", register));
                        Instructions.Insert(index++,
                            string.Format("move-result {0}", register));

                        return;
                    }
                case "B":
                    {
                        Instructions.Insert(index++,
                            string.Format("check-cast {0}, Ljava/lang/Byte;", register));
                        Instructions.Insert(index++,
                            string.Format("invoke-virtual {{{0}}}, Ljava/lang/Byte;->byteValue()B", register));
                        Instructions.Insert(index++,
                            string.Format("move-result {0}", register));

                        return;
                    }
            }

            throw new Exception("Unsupported primitive value: " + primitiveType);
        }

        public void AddHookBefore(MethodToHook hook)
        {
            IsPatched = true;

            int index = 0;

            Instructions.Insert(index++, "new-instance v0, Lcom/xquadplaystatic/MethodHookParam;");
            Instructions.Insert(index++, "invoke-direct {v0}, Lcom/xquadplaystatic/MethodHookParam;-><init>()V");

            if (!IsStatic)
            {
                Instructions.Insert(index++, "iput-object p0, v0, Lcom/xquadplaystatic/MethodHookParam;->thisObject:Ljava/lang/Object;");
            }

            if (ParameterTypes.Count > 0)
            {
                Instructions.Insert(index++, "const v1, 0x" + ParameterTypes.Count.ToString("x"));
                Instructions.Insert(index++, "new-array v1, v1, [Ljava/lang/Object;");
                Instructions.Insert(index++, "iput-object v1, v0, Lcom/xquadplaystatic/MethodHookParam;->args:[Ljava/lang/Object;");

                for (int n = 0; n < ParameterTypes.Count; ++n)
                {
                    string paramType = ParameterTypes[n];
                    string paramRegister = "p" + (n + (IsStatic ? 0 : 1));

                    if (!IsObjectTypeTrueObject(paramType))
                    {
                        PackPrimitiveValue(ref index, paramType, paramRegister);
                    }

                    Instructions.Insert(index++, "iget-object v1, v0, Lcom/xquadplaystatic/MethodHookParam;->args:[Ljava/lang/Object;");
                    Instructions.Insert(index++, "const v2, 0x" + n.ToString("x"));
                    Instructions.Insert(index++, string.Format("aput-object {0}, v1, v2", paramRegister));
                }
            }

            Instructions.Insert(index++,
                string.Format("invoke-static {{v0}}, {0}->{1}(Lcom/xquadplaystatic/MethodHookParam;)V",
                hook.Interceptor.ParentClass.ClassName,
                hook.Interceptor.MethodName));

            if (ParameterTypes.Count > 0)
            {
                for (int n = 0; n < ParameterTypes.Count; ++n)
                {
                    string paramType = ParameterTypes[n];
                    string paramRegister = "p" + (n + (IsStatic ? 0 : 1));

                    Instructions.Insert(index++, "iget-object v1, v0, Lcom/xquadplaystatic/MethodHookParam;->args:[Ljava/lang/Object;");
                    Instructions.Insert(index++, "const v2, 0x" + n.ToString("x"));
                    Instructions.Insert(index++, string.Format("aget-object {0}, v1, v2", paramRegister));
                }

                AddParameterMirroringCode(ref index);

                for (int n = 0; n < ParameterTypes.Count; ++n)
                {
                    string paramType = ParameterTypes[n];
                    string paramRegister = "p" + (n + (IsStatic ? 0 : 1));

                    if (!IsObjectTypeTrueObject(paramType))
                    {
                        UnpackPrimitiveValue(ref index, paramType, paramRegister);
                    }
                }
            }

            Instructions.Insert(index++, "iget-boolean v1, v0, Lcom/xquadplaystatic/MethodHookParam;->returnEarly:Z");
            Instructions.Insert(index++, "if-eqz v1, :cond_normal_run");

            //if we canceled executing method by calling setResult()
            if (ReturnType == "V")
            {
                Instructions.Insert(index++, "return-void");
            }
            else if (IsObjectTypeTrueObject(ReturnType))
            {
                Instructions.Insert(index++, "invoke-virtual {v0}, Lcom/xquadplaystatic/MethodHookParam;->getResult()Ljava/lang/Object;");
                Instructions.Insert(index++, "move-result-object v1");
                Instructions.Insert(index++, string.Format("check-cast v1, {0}", ReturnType));
                Instructions.Insert(index++, "return-object v1");
            }
            else
            {
                Instructions.Insert(index++, "invoke-virtual {v0}, Lcom/xquadplaystatic/MethodHookParam;->getResult()Ljava/lang/Object;");
                Instructions.Insert(index++, "move-result-object v1");
                UnpackPrimitiveValue(ref index, ReturnType, "v1");
                Instructions.Insert(index++, "return v1");
            }

            Instructions.Insert(index++, ":cond_normal_run");
        }

        bool IsObjectTypeTrueObject(string type)
        {
            return type.StartsWith("L") || type.StartsWith("[");
        }

        public void InjectAfterHookCode(MethodToHook hook, ref int index, bool hasReturnValue)
        {
            Instructions.Insert(index++, "new-instance v0, Lcom/xquadplaystatic/MethodHookParam;");
            Instructions.Insert(index++, "invoke-direct {v0}, Lcom/xquadplaystatic/MethodHookParam;-><init>()V");

            if (hasReturnValue)
            {
                Instructions.Insert(index++, "invoke-virtual {v0, v1}, Lcom/xquadplaystatic/MethodHookParam;->setResult(Ljava/lang/Object;)V");
            }

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
                        PackPrimitiveValue(ref index, ReturnType, "v1");
                    }

                    InjectAfterHookCode(hook, ref index, true);

                    if (returnValRegister != "v1")
                    {
                        UnpackPrimitiveValue(ref index, ReturnType, "v1");
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

        bool calledAlready;

        public string GetModifiedCode()
        {
            //if (calledAlready)
            //    throw new Exception("Can only be called once!");
            //calledAlready = true;

            //AddParameterMirroringCode();

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("#modified");

            int extendedRegisters = OriginalAllocatedRegisters + 4 + ParameterTypes.Count;

            string modifiedPrologue = prologueSource
                .Replace(
                string.Format(".registers {0}", OriginalAllocatedRegisters),
                string.Format(".registers {0}", extendedRegisters));

            sb.AppendLine(modifiedPrologue);

            foreach (var ins in Instructions)
            {
                sb.AppendLine("    " + ins);
            }

            sb.AppendLine(".end method");
            sb.AppendLine("#modified");

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

                        if (trimmed.StartsWith(".registers"))
                        {
                            int regCount = int.Parse(trimmed.Split(' ')[1]);
                            AllocatedRegisters = regCount;
                            OriginalAllocatedRegisters = AllocatedRegisters;
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
            RawParameterLine = header.Substring(firstParenthesis + 1, lastParenthesis - (firstParenthesis + 1));

            ParseParameterLine(RawParameterLine);
        }

        void ParseParameterLine(string rawParameters)
        {
            var reader = new StringReader(rawParameters);

            bool processingObjectName = false;
            var objectNameSb = new StringBuilder();

            int nextChar;
            while ((nextChar = reader.Read()) != -1)
            {
                char c = (char)nextChar;
                objectNameSb.Append(c);

                if (processingObjectName)
                {
                    if (c == ';')
                    {
                        processingObjectName = false;
                        var name = objectNameSb.ToString();
                        objectNameSb.Clear();
                        ParameterTypes.Add(name);
                    }
                }
                else
                {
                    if (IsObjectTypeTrueObject(c.ToString()))
                    {
                        processingObjectName = true;
                    }
                    else
                    {
                        var name = objectNameSb.ToString();
                        objectNameSb.Clear();
                        ParameterTypes.Add(name);
                    }
                }
            }
        }
    }
}
