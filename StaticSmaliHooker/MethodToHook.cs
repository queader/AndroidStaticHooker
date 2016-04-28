using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StaticSmaliHooker
{
    class MethodToHook
    {
        public string TargetClass { get; private set; }
        public string TargetClassFormatted { get; private set; }
        public string TargetMethod { get; private set; }
        public SmaliMethod Interceptor { get; private set; }

        public bool HookBefore { get; private set; }
        public bool HookAfter { get; private set; }
        public bool? IsStatic { get; private set; }
        public string MethodName { get; private set; }
        public bool IgnoreArgs { get; private set; }

        public MethodToHook(SmaliAnnotation annot, SmaliMethod interceptor)
        {
            TargetClass = annot.Properties["clazz"].Trim();
            TargetMethod = annot.Properties["method"].Trim();
            Interceptor = interceptor;

            TargetClassFormatted = string.Format("L{0};", TargetClass.Replace('.', '/'));

            string[] split = TargetMethod.Split(' ');

            string when = split[0].Trim();
            if (when == "after")
                HookAfter = true;
            else if (when == "before")
                HookBefore = true;
            else
                throw new Exception("Not specified when to hook method");

            if (split.Length >= 3)
            {
                if (split[1].Trim() == "static")
                    IsStatic = true;
                else if (split[1].Trim() == "nostatic")
                    IsStatic = false;
            }

            string signature = split[split.Length - 1].Trim();

            int firstParenthesis = signature.IndexOf('(');
            if (firstParenthesis >= 0)
            {

            }
            else
            {
                MethodName = signature;
            }
        }

        public void Print()
        {
            Console.WriteLine("   Found Method Hook: {0} for: {1}->{2}", Interceptor.MethodName, TargetClass, TargetMethod);
        }

        public override string ToString()
        {
            return string.Format("{0} for: {1}->{2}", Interceptor.MethodName, TargetClass, TargetMethod);
        }

        public bool IsMethodAdequate(SmaliMethod method)
        {
            if (method.MethodName != MethodName)
            {
                return false;
            }

            if (IsStatic.HasValue)
            {
                return method.IsStatic == IsStatic;
            }

            return true;
        }
    }
}
