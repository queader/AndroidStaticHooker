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
        public bool IsPrivate { get; private set; }
        public bool IsFinal { get; private set; }
        public List<SmaliAnnotation> Annotations { get; private set; }
        public List<string> Instructions { get; private set; }
        public List<string> ParameterTypes { get; private set; }
        public int OriginalAllocatedRegisters { get; private set; }
        public int OriginalAllocatedLocals { get; private set; }
        public string RawParameterLine { get; private set; }

        public string OriginalHeaderLine { get; private set; }

        string prologueSource;
        MethodToHook hookBefore;
        MethodToHook hookAfter;

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

            return string.Format("{0} ({1}) : {2}", MethodName, sb, ReturnType);
        }

        public void AddHookBefore(MethodToHook hook)
        {
            IsPatched = true;
            hookBefore = hook;
        }

        public void AddHookAfter(MethodToHook hook)
        {
            IsPatched = true;
            hookAfter = hook;
        }

        string GenerateHookMethod()
        {
            StringBuilder sb = new StringBuilder();

            int paramRegisterCount = 0;
            for (int n = 0; n < ParameterTypes.Count; ++n)
            {
                string paramType = ParameterTypes[n];
                paramRegisterCount += IsWidePrimitive(paramType) ? 2 : 1;
            }

            int extendedRegisters = 4 + paramRegisterCount;

            string modifiedPrologue = prologueSource
                .Replace(
                    string.Format(".registers {0}", OriginalAllocatedRegisters),
                    string.Format(".registers {0}", extendedRegisters))
                .Replace(
                    string.Format(".locals {0}", OriginalAllocatedLocals),
                    string.Format(".locals {0}", 4));

            sb.AppendLine(modifiedPrologue);

            if (hookBefore != null)
            {
                sb.AppendLine("new-instance v0, Lcom/xquadplaystatic/MethodHookParam;");
                sb.AppendLine("invoke-direct {v0}, Lcom/xquadplaystatic/MethodHookParam;-><init>()V");

                if (!IsStatic)
                    sb.AppendLine("iput-object p0, v0, Lcom/xquadplaystatic/MethodHookParam;->thisObject:Ljava/lang/Object;");

                if (ParameterTypes.Count > 0)
                {
                    sb.AppendLine("const v1, 0x" + ParameterTypes.Count.ToString("x"));
                    sb.AppendLine("new-array v1, v1, [Ljava/lang/Object;");
                    sb.AppendLine("iput-object v1, v0, Lcom/xquadplaystatic/MethodHookParam;->args:[Ljava/lang/Object;");

                    int currentParamRegister = (IsStatic ? 0 : 1);
                    for (int n = 0; n < ParameterTypes.Count; ++n)
                    {
                        string paramType = ParameterTypes[n];
                        string paramRegister = "p" + currentParamRegister;

                        if (!IsObjectTypeTrueObject(paramType))
                            PackPrimitiveValue(sb, paramType, paramRegister);

                        //sb.AppendLine("iget-object v1, v0, Lcom/xquadplaystatic/MethodHookParam;->args:[Ljava/lang/Object;");
                        sb.AppendLine("const v2, 0x" + n.ToString("x"));
                        sb.AppendLine(string.Format("aput-object {0}, v1, v2", paramRegister));

                        currentParamRegister += IsWidePrimitive(paramType) ? 2 : 1;
                    }
                }

                sb.AppendLine(
                    string.Format("invoke-static {{v0}}, {0}->{1}(Lcom/xquadplaystatic/MethodHookParam;)V",
                        hookBefore.Interceptor.ParentClass.ClassName,
                        hookBefore.Interceptor.MethodName));

                if (ParameterTypes.Count > 0)
                {
                    sb.AppendLine("iget-object v1, v0, Lcom/xquadplaystatic/MethodHookParam;->args:[Ljava/lang/Object;");

                    int currentParamRegister = (IsStatic ? 0 : 1);
                    for (int n = 0; n < ParameterTypes.Count; ++n)
                    {
                        string paramType = ParameterTypes[n];
                        string paramRegister = "p" + currentParamRegister;

                        //sb.AppendLine("iget-object v1, v0, Lcom/xquadplaystatic/MethodHookParam;->args:[Ljava/lang/Object;");
                        sb.AppendLine("const v2, 0x" + n.ToString("x"));
                        sb.AppendLine(string.Format("aget-object {0}, v1, v2", paramRegister));

                        if (!IsObjectTypeTrueObject(paramType))
                            UnpackPrimitiveValue(sb, paramType, paramRegister);
                        else
                            sb.AppendLine(string.Format("check-cast {0}, {1}", paramRegister, paramType));

                        currentParamRegister += IsWidePrimitive(paramType) ? 2 : 1;
                    }
                }

                sb.AppendLine("iget-boolean v1, v0, Lcom/xquadplaystatic/MethodHookParam;->returnEarly:Z");
                sb.AppendLine("if-eqz v1, :cond_normal_run");

                if (ReturnType == "V")
                {
                    sb.AppendLine("return-void");
                }
                else if (IsObjectTypeTrueObject(ReturnType))
                {
                    sb.AppendLine("invoke-virtual {v0}, Lcom/xquadplaystatic/MethodHookParam;->getResult()Ljava/lang/Object;");
                    sb.AppendLine("move-result-object v1");
                    sb.AppendLine(string.Format("check-cast v1, {0}", ReturnType));
                    sb.AppendLine("return-object v1");
                }
                else
                {
                    sb.AppendLine("invoke-virtual {v0}, Lcom/xquadplaystatic/MethodHookParam;->getResult()Ljava/lang/Object;");
                    sb.AppendLine("move-result-object v1");
                    UnpackPrimitiveValue(sb, ReturnType, "v1");
                    sb.AppendLine("return v1");
                }

                sb.AppendLine(":cond_normal_run");
            }

            sb.AppendLine();
            if (IsStatic)
            {
                if (paramRegisterCount > 1)
                {
                    sb.AppendLine(
                        string.Format("invoke-static/range {{p0 .. {4}}}, {0}->{1}({2}){3}",
                        ParentClass.ClassName,
                        GetHookedMethodName(),
                        RawParameterLine,
                        ReturnType,
                        "p" + (paramRegisterCount - 1)));
                }
                else if (paramRegisterCount == 1)
                {
                    sb.AppendLine(
                        string.Format("invoke-static {{p0}}, {0}->{1}({2}){3}",
                        ParentClass.ClassName,
                        GetHookedMethodName(),
                        RawParameterLine,
                        ReturnType));
                }
                else
                {
                    sb.AppendLine(
                        string.Format("invoke-static {{}}, {0}->{1}({2}){3}",
                        ParentClass.ClassName,
                        GetHookedMethodName(),
                        RawParameterLine,
                        ReturnType));
                }
            }
            else
            {
                string invokeType = (IsPrivate || IsConstructor) ? "invoke-direct" : "invoke-virtual";

                if (paramRegisterCount == 0)
                {
                    sb.AppendLine(
                        string.Format("{4} {{p0}}, {0}->{1}({2}){3}",
                        ParentClass.ClassName,
                        GetHookedMethodName(),
                        RawParameterLine,
                        ReturnType,
                        invokeType));
                }
                else
                {
                    sb.AppendLine(
                        string.Format("{5}/range {{p0 .. {4}}}, {0}->{1}({2}){3}",
                        ParentClass.ClassName,
                        GetHookedMethodName(),
                        RawParameterLine,
                        ReturnType,
                        "p" + (paramRegisterCount),
                        invokeType));
                }
            }
            sb.AppendLine();

            if (ReturnType != "V")
            {
                if (IsObjectTypeTrueObject(ReturnType))
                    sb.AppendLine("move-result-object v1");
                else
                    sb.AppendLine("move-result v1");
            }

            if (hookAfter == null)
            {
                if (ReturnType == "V")
                {
                    sb.AppendLine("return-void");
                }
                else if (IsObjectTypeTrueObject(ReturnType))
                {
                    sb.AppendLine("return-object v1");
                }
                else
                {
                    sb.AppendLine("return v1");
                }
            }
            else
            {
                sb.AppendLine("new-instance v0, Lcom/xquadplaystatic/MethodHookParam;");
                sb.AppendLine("invoke-direct {v0}, Lcom/xquadplaystatic/MethodHookParam;-><init>()V");

                if (!IsStatic)
                    sb.AppendLine("iput-object p0, v0, Lcom/xquadplaystatic/MethodHookParam;->thisObject:Ljava/lang/Object;");

                if (ReturnType != "V")
                {
                    if (!IsObjectTypeTrueObject(ReturnType))
                        PackPrimitiveValue(sb, ReturnType, "v1");

                    sb.AppendLine("invoke-virtual {v0, v1}, Lcom/xquadplaystatic/MethodHookParam;->setResult(Ljava/lang/Object;)V");
                }

                if (ParameterTypes.Count > 0)
                {
                    sb.AppendLine("const v1, 0x" + ParameterTypes.Count.ToString("x"));
                    sb.AppendLine("new-array v1, v1, [Ljava/lang/Object;");
                    sb.AppendLine("iput-object v1, v0, Lcom/xquadplaystatic/MethodHookParam;->args:[Ljava/lang/Object;");

                    int currentParamRegister = (IsStatic ? 0 : 1);
                    for (int n = 0; n < ParameterTypes.Count; ++n)
                    {
                        string paramType = ParameterTypes[n];
                        string paramRegister = "p" + currentParamRegister;

                        if (!IsObjectTypeTrueObject(paramType))
                            PackPrimitiveValue(sb, paramType, paramRegister);

                        //sb.AppendLine("iget-object v1, v0, Lcom/xquadplaystatic/MethodHookParam;->args:[Ljava/lang/Object;");
                        sb.AppendLine("const v2, 0x" + n.ToString("x"));
                        sb.AppendLine(string.Format("aput-object {0}, v1, v2", paramRegister));

                        currentParamRegister += IsWidePrimitive(paramType) ? 2 : 1;
                    }
                }

                sb.AppendLine(
                    string.Format("invoke-static {{v0}}, {0}->{1}(Lcom/xquadplaystatic/MethodHookParam;)V",
                    hookAfter.Interceptor.ParentClass.ClassName,
                    hookAfter.Interceptor.MethodName));

                if (ReturnType == "V")
                {
                    sb.AppendLine("return-void");
                }
                else if (IsObjectTypeTrueObject(ReturnType))
                {
                    sb.AppendLine("invoke-virtual {v0}, Lcom/xquadplaystatic/MethodHookParam;->getResult()Ljava/lang/Object;");
                    sb.AppendLine("move-result-object v1");
                    sb.AppendLine(string.Format("check-cast v1, {0}", ReturnType));
                    sb.AppendLine("return-object v1");
                }
                else
                {
                    sb.AppendLine("invoke-virtual {v0}, Lcom/xquadplaystatic/MethodHookParam;->getResult()Ljava/lang/Object;");
                    sb.AppendLine("move-result-object v1");
                    UnpackPrimitiveValue(sb, ReturnType, "v1");
                    sb.AppendLine("return v1");
                }
            }

            sb.AppendLine(".end method");

            return sb.ToString();
        }

        string GetHookedMethodName()
        {
            return (MethodName + "_hooked")
                .Replace("<", "")
                .Replace(">", "");
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

        void PackPrimitiveValue(ref int index, string primitiveType, string register)
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

        void PackPrimitiveValue(StringBuilder sb, string primitiveType, string register)
        {
            switch (primitiveType)
            {
                case "I":
                    {
                        sb.AppendLine(
                            string.Format("invoke-static {{{0}}}, Ljava/lang/Integer;->valueOf(I)Ljava/lang/Integer;", register));
                        sb.AppendLine(
                            string.Format("move-result-object {0}", register));

                        return;
                    }
                case "Z":
                    {
                        sb.AppendLine(
                            string.Format("invoke-static {{{0}}}, Ljava/lang/Boolean;->valueOf(Z)Ljava/lang/Boolean;", register));
                        sb.AppendLine(
                            string.Format("move-result-object {0}", register));

                        return;
                    }
                case "B":
                    {
                        sb.AppendLine(
                            string.Format("invoke-static {{{0}}}, Ljava/lang/Byte;->valueOf(B)Ljava/lang/Byte;", register));
                        sb.AppendLine(
                            string.Format("move-result-object {0}", register));

                        return;
                    }
                case "J":
                    {
                        int regIndex = int.Parse(register.Substring(1));
                        string nextReg = register.Substring(0, 1) + (regIndex + 1);

                        sb.AppendLine(
                           string.Format("invoke-static {{{0}, {1}}}, Ljava/lang/Long;->valueOf(J)Ljava/lang/Long;", register, nextReg));
                        sb.AppendLine(
                            string.Format("move-result-object {0}", register));

                        return;
                    }
            }

            throw new Exception("Unsupported primitive value: " + primitiveType);
        }

        void UnpackPrimitiveValue(ref int index, string primitiveType, string register)
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

        void UnpackPrimitiveValue(StringBuilder sb, string primitiveType, string register)
        {
            switch (primitiveType)
            {
                case "I":
                    {
                        sb.AppendLine(
                            string.Format("check-cast {0}, Ljava/lang/Integer;", register));
                        sb.AppendLine(
                            string.Format("invoke-virtual {{{0}}}, Ljava/lang/Integer;->intValue()I", register));
                        sb.AppendLine(
                            string.Format("move-result {0}", register));

                        return;
                    }
                case "Z":
                    {
                        sb.AppendLine(
                            string.Format("check-cast {0}, Ljava/lang/Boolean;", register));
                        sb.AppendLine(
                            string.Format("invoke-virtual {{{0}}}, Ljava/lang/Boolean;->booleanValue()Z", register));
                        sb.AppendLine(
                            string.Format("move-result {0}", register));

                        return;
                    }
                case "B":
                    {
                        sb.AppendLine(
                             string.Format("check-cast {0}, Ljava/lang/Byte;", register));
                        sb.AppendLine(
                            string.Format("invoke-virtual {{{0}}}, Ljava/lang/Byte;->byteValue()B", register));
                        sb.AppendLine(
                            string.Format("move-result {0}", register));

                        return;
                    }
                case "J":
                    {
                        int regIndex = int.Parse(register.Substring(1));
                        string nextReg = register.Substring(0, 1) + (regIndex + 1);

                        sb.AppendLine(
                            string.Format("check-cast {0}, Ljava/lang/Long;", register));
                        sb.AppendLine(
                           string.Format("invoke-virtual {{{0}}}, Ljava/lang/Long;->longValue()J", register));
                        sb.AppendLine(
                            string.Format("move-result-wide {0}", register));

                        return;
                    }
            }

            throw new Exception("Unsupported primitive value: " + primitiveType);
        }

        public void AddHookBeforeOld(MethodToHook hook)
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

        bool IsWidePrimitive(string type)
        {
            return type == "J" || type == "D";
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

        public void AddHookAfterOld(MethodToHook hook)
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

        public string GetModifiedCode()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("#modified");

            //int extendedRegisters = OriginalAllocatedRegisters + 4 + ParameterTypes.Count;

            //string modifiedPrologue = prologueSource
            //    .Replace(
            //    string.Format(".registers {0}", OriginalAllocatedRegisters),
            //    string.Format(".registers {0}", extendedRegisters));

            //sb.AppendLine(modifiedPrologue);

            //foreach (var ins in Instructions)
            //{
            //    sb.AppendLine("    " + ins);
            //}

            //sb.AppendLine(".end method");

            sb.AppendLine(GenerateHookMethod());
            sb.AppendLine();

            string modifiedHeaderLine = OriginalHeaderLine
                .Replace(MethodName, GetHookedMethodName())
                .Replace("constructor", "");

            sb.AppendLine(OriginalSource
                .Replace(OriginalHeaderLine, modifiedHeaderLine));

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
                        if (trimmed.StartsWith(".prologue"))
                        {
                            prologueFinished = true;
                        }

                        if (trimmed.StartsWith(".registers"))
                        {
                            int regCount = int.Parse(trimmed.Split(' ')[1]);
                            OriginalAllocatedRegisters = regCount;
                        }
                        if (trimmed.StartsWith(".locals"))
                        {
                            int regCount = int.Parse(trimmed.Split(' ')[1]);
                            OriginalAllocatedLocals = regCount;
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

                ParseAnnotations(prologueSource);
            }
        }

        void ParseAnnotations(string prologue)
        {
            using (var reader = new StringReader(prologue))
            {
                string nextLine = null;
                while ((nextLine = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrEmpty(nextLine))
                        continue;

                    string trimmed = nextLine.Trim();

                    if (trimmed.StartsWith(".annotation"))
                    {
                        string annotationBody = GetBodyToEndTag(reader, nextLine);
                        CreateAnnotation(annotationBody);
                    }
                }
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
            OriginalHeaderLine = header;

            IsStatic = header.Contains(" static "); //whitespaces so we get no fake positives from method name
            IsConstructor = header.Contains(" constructor ");
            IsPrivate = header.Contains(" private ");
            IsFinal = header.Contains(" final ");

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
            bool previousCharArrayBracket = false;

            var objectNameSb = new StringBuilder();

            int nextChar;
            while ((nextChar = reader.Read()) != -1)
            {
                char c = (char)nextChar;
                objectNameSb.Append(c);

                if (processingObjectName)
                {
                    if (previousCharArrayBracket)
                    {
                        if (c == '[')
                        {
                            previousCharArrayBracket = true;
                        }
                        else if (!IsObjectTypeTrueObject(c.ToString()))
                        {
                            processingObjectName = false;
                            var name = objectNameSb.ToString();
                            objectNameSb.Clear();
                            ParameterTypes.Add(name);

                            previousCharArrayBracket = false;
                        }
                        else
                        {
                            previousCharArrayBracket = false;
                        }
                    }
                    else if (c == ';')
                    {
                        processingObjectName = false;
                        var name = objectNameSb.ToString();
                        objectNameSb.Clear();
                        ParameterTypes.Add(name);
                    }
                }
                else
                {
                    if (c == '[')
                    {
                        processingObjectName = true;
                        previousCharArrayBracket = true;
                    }
                    else if (IsObjectTypeTrueObject(c.ToString()))
                    {
                        processingObjectName = true;
                        previousCharArrayBracket = false;
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
