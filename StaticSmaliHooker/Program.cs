using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StaticSmaliHooker
{
    class Program
    {
        public static Random Random = new Random();

        static List<UnpackedApp> unpackedAppList = new List<UnpackedApp>();

        class UnpackedApp
        {
            public string OriginalJarPath { get; set; }
            public string UnpackedPath { get; set; }
            public List<string> BaksmailedDexPaths { get; set; }
            public Dictionary<string, string> BaksmailedDexNames { get; set; }
            public List<string> CompiledDexPaths { get; set; }
            public bool OnlyCopyDex { get; set; }

            public UnpackedApp()
            {
                BaksmailedDexPaths = new List<string>();
                BaksmailedDexNames = new Dictionary<string, string>();
                CompiledDexPaths = new List<string>();
            }
        }

        static Dictionary<string, SmaliClass> parsedClasses = new Dictionary<string, SmaliClass>();
        static List<MethodToHook> methodsToHook = new List<MethodToHook>();
        static HashSet<SmaliClass> dirtyClasses = new HashSet<SmaliClass>();

        static bool singleDex;

        static void Main(string[] args)
        {
            Console.WriteLine("Usage <app1> [-dex-only] <app2> ...");

            bool onlyCopyDexFlag = false;

            foreach (var arg in args)
            {
                if (arg == "-dex-only")
                {
                    onlyCopyDexFlag = true;
                }
                else if (arg == "-single-dex")
                {
                    singleDex = true;
                }
                else
                {
                    string lib = Path.GetFullPath(arg);
                    unpackedAppList.Add(new UnpackedApp
                    {
                        OriginalJarPath = lib,
                        OnlyCopyDex = onlyCopyDexFlag,
                    });

                    onlyCopyDexFlag = false;
                }
            }

            Console.WriteLine("\nCleaning...");
            CleanDirectory(@"TempSmali");

            Console.WriteLine("\nUnpacking...");
            Directory.CreateDirectory(@"TempSmali");
            Directory.CreateDirectory(@"TempSmali\Unpacked");

            foreach (var app in unpackedAppList)
                UnpackJar(app);

            Console.WriteLine("\nBaksmaling...");
            Directory.CreateDirectory(@"TempSmali\Baksmalied");

            foreach (var app in unpackedAppList)
            {
                foreach (string dexFile in Directory.EnumerateFiles(app.UnpackedPath, "*.dex"))
                {
                    RunBaksmali(app, dexFile);
                }
            }

            Console.WriteLine("\nParsing...");

            foreach (var app in unpackedAppList)
            {
                foreach (string baksmailedDex in app.BaksmailedDexPaths)
                {
                    ParseClasses(baksmailedDex);

                    if (singleDex)
                        break;
                }

                if (singleDex)
                    break;
            }

            Console.WriteLine("\nHooking...");

            foreach (var hook in methodsToHook)
            {
                ExecuteHook(hook);
            }

            Console.WriteLine("\nGenerating Code...");

            foreach (var dirty in dirtyClasses)
            {
                GenerateCodeForDirtyClass(dirty);
            }

            Console.WriteLine("\nSmaling...");
            Directory.CreateDirectory(@"TempSmali\Smalied");

            int dexindex = 1;

            foreach (var app in unpackedAppList)
            {
                foreach (string baksmailedDex in app.BaksmailedDexPaths)
                {
                    RunSmali(app, baksmailedDex, dexindex);
                    ++dexindex;

                    if (singleDex)
                        break;
                }

                if (singleDex)
                    break;
            }

            Console.WriteLine("\nMerging...");
            Directory.CreateDirectory(@"TempSmali\Merged");

            foreach (var app in unpackedAppList)
            {
                CopyToMerge(app);
            }

            foreach (var app in unpackedAppList)
            {
                foreach (var dex in app.CompiledDexPaths)
                {
                    CopyDexToMerge(dex);
                }
            }

            Console.WriteLine("\nPackaging...");

            string finalName = string.Format("{0}-hooked{1}",
                Path.GetFileNameWithoutExtension(unpackedAppList[0].OriginalJarPath),
                Path.GetExtension(unpackedAppList[0].OriginalJarPath));

            CreateFinalPackage(finalName);

        }

        static void GenerateCodeForDirtyClass(SmaliClass clazz)
        {
            Console.WriteLine("   Generating Code for: {0}", clazz.ClassName);

            clazz.CheckAndPatch();
        }

        static void ExecuteHook(MethodToHook hook)
        {
            Console.WriteLine("   Executing Method Hook: {0}", hook);

            SmaliClass targetClass;
            parsedClasses.TryGetValue(hook.TargetClassFormatted, out targetClass);

            if (targetClass == null)
            {
                Console.WriteLine("      Class Not Found! Skipping: {0}", hook.TargetClassFormatted);
                return;
            }

            foreach (var method in targetClass.Methods)
            {
                if (hook.IsMethodAdequate(method))
                {
                    Console.WriteLine("      Found Adequate Method: {0}", method);

                    if (hook.HookAfter)
                    {
                        method.AddHookAfter(hook);
                    }

                    if (hook.HookBefore)
                    {
                        method.AddHookBefore(hook);
                    }

                    //method.PrintInstructions();

                    dirtyClasses.Add(targetClass);
                }
            }
        }

        public static void RegisterMethodToHook(MethodToHook toHook)
        {
            toHook.Print();
            methodsToHook.Add(toHook);
        }

        static void ParseClasses(string dir)
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.smali"))
            {
                var smaliClass = new SmaliClass() { SourcePath = file };
                smaliClass.Parse();

                parsedClasses[smaliClass.ClassName] = smaliClass;
            }

            foreach (var subdir in Directory.EnumerateDirectories(dir))
            {
                ParseClasses(subdir);
            }
        }

        static void CreateFinalPackage(string name)
        {
            name = Path.GetFullPath(name);

            if (File.Exists(name))
            {
                Console.WriteLine("   Removing Existing: {0}", name);
                File.Delete(name);
            }

            Console.WriteLine("   Creating Package: {0}", name);
            ZipFile.CreateFromDirectory(@"TempSmali\Merged\", name, CompressionLevel.NoCompression, false);
        }

        static void CopyDexToMerge(string path)
        {
            string targetPath = string.Format(@"TempSmali\Merged\{0}", Path.GetFileName(path));
            targetPath = Path.GetFullPath(targetPath);

            Console.WriteLine("   Merging Dex: {0} to: {1}", path, targetPath);

            File.Copy(path, targetPath);
        }

        static void CopyToMerge(UnpackedApp app)
        {
            if (app.OnlyCopyDex)
            {
                Console.WriteLine("   Skipping File Merge for: {0} because -dex-only flag is set", app.UnpackedPath);
                return;
            }

            string targetPath = string.Format(@"TempSmali\Merged\");
            targetPath = Path.GetFullPath(targetPath);

            Console.WriteLine("   Merging: {0} to: {1}", app.UnpackedPath, targetPath);

            DirectoryCopy(app.UnpackedPath, targetPath, true, false);
        }

        static void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs, bool debugInfo)
        {
            if (debugInfo)
                Console.WriteLine("      Copying Directory: {0} to: {1}", sourceDirName, destDirName);

            // Get the subdirectories for the specified directory.
            DirectoryInfo dir = new DirectoryInfo(sourceDirName);

            if (!dir.Exists)
            {
                throw new DirectoryNotFoundException(
                    "Source directory does not exist or could not be found: "
                    + sourceDirName);
            }

            DirectoryInfo[] dirs = dir.GetDirectories();
            // If the destination directory doesn't exist, create it.
            if (!Directory.Exists(destDirName))
            {
                Directory.CreateDirectory(destDirName);
            }

            // Get the files in the directory and copy them to the new location.
            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                if (file.Extension == ".dex")
                {
                    if (debugInfo)
                        Console.WriteLine("         Ignoring: {0}", file.FullName);
                    continue;
                }

                string temppath = Path.Combine(destDirName, file.Name);

                if (!File.Exists(temppath))
                {
                    if (debugInfo)
                        Console.WriteLine("         Copying: {0} to: {1}", file.FullName, temppath);
                    file.CopyTo(temppath, false);
                }
                else
                {
                    if (debugInfo)
                        Console.WriteLine("         Skipping (Exists): {0} to: {1}", file.FullName, temppath);
                }
            }

            // If copying subdirectories, copy them and their contents to new location.
            if (copySubDirs)
            {
                foreach (DirectoryInfo subdir in dirs)
                {
                    string temppath = Path.Combine(destDirName, subdir.Name);
                    DirectoryCopy(subdir.FullName, temppath, copySubDirs, debugInfo);
                }
            }
        }

        static void RunSmali(UnpackedApp app, string baksmailedDex, int dexIndex)
        {
            string dexName = dexIndex == 1 ? "classes.dex" : string.Format("classes{0}.dex", dexIndex);
            string targetPath = string.Format(@"TempSmali\Smalied\{0}", dexName);
            targetPath = Path.GetFullPath(targetPath);

            Console.WriteLine("   Smailing: {0} to: {1}", baksmailedDex, targetPath);

            var processInfo = new ProcessStartInfo(Path.GetFullPath("smali.bat"), string.Format(@"-o {0} {1}", targetPath, baksmailedDex))
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            };

            Process.Start(processInfo).WaitForExit();

            app.CompiledDexPaths.Add(targetPath);
        }

        static void CleanDirectory(string path)
        {
            path = Path.GetFullPath(path);
            Console.WriteLine("   Deleting Directory: " + path);

            var processInfo = new ProcessStartInfo("cmd.exe", string.Format(@"/c rmdir /s/q {0}", path))
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            };

            Process.Start(processInfo).WaitForExit();
        }

        static void RunBaksmali(UnpackedApp app, string dexPath)
        {
            string jarName = Path.GetFileNameWithoutExtension(app.OriginalJarPath);
            string dexName = Path.GetFileNameWithoutExtension(dexPath);
            string targetPath = string.Format(@"TempSmali\Baksmalied\{0}\{1}\", jarName, dexName);

            if (singleDex)
                targetPath = string.Format(@"TempSmali\Baksmalied\SingleDex\");

            targetPath = Path.GetFullPath(targetPath);

            Console.WriteLine("   Baksmaling: {0} to: {1}", dexPath, targetPath);

            Directory.CreateDirectory(targetPath);

            var processInfo = new ProcessStartInfo(Path.GetFullPath("baksmali.bat"), string.Format(@"-o {0} {1}", targetPath, dexPath))
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            };

            Process.Start(processInfo).WaitForExit();

            app.BaksmailedDexPaths.Add(targetPath);
            app.BaksmailedDexNames[targetPath] = dexName;
        }

        static void UnpackJar(UnpackedApp app)
        {
            string jarName = Path.GetFileNameWithoutExtension(app.OriginalJarPath);
            string targetPath = string.Format(@"TempSmali\Unpacked\{0}\", jarName);
            targetPath = Path.GetFullPath(targetPath);

            Console.WriteLine("   Unpacking: {0} to: {1}", app.OriginalJarPath, targetPath);

            Directory.CreateDirectory(targetPath);
            ZipFile.ExtractToDirectory(app.OriginalJarPath, targetPath);

            app.UnpackedPath = targetPath;
        }
    }
}
