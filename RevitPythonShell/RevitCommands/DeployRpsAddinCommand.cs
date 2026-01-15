using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Resources;
using System.Text;
using System.Windows.Forms;
using System.Xml.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using RpsRuntime;

namespace RevitPythonShell.RevitCommands
{
    /// <summary>
    /// Ask the user for an RpsAddin xml file. Create a subfolder
    /// with timestamp containing the deployable version of the RPS scripts.
    /// 
    /// This includes the RpsRuntime.dll (see separate project) that recreates some
    /// of the RPS experience for canned commands.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class DeployRpsAddinCommand: IExternalCommand
    {
        private string _outputFolder;
        private string _rootFolder;
        private string _addinName;
        private XDocument _doc;

        Result IExternalCommand.Execute(ExternalCommandData commandData, ref string message, Autodesk.Revit.DB.ElementSet elements)
        {
            try
            {
                // read in rpsaddin.xml
                var rpsAddinXmlPath = GetAddinXmlPath(); // FIXME: do some argument checking here            

                _addinName = Path.GetFileNameWithoutExtension(rpsAddinXmlPath);
                _rootFolder = Path.GetDirectoryName(rpsAddinXmlPath);

                _doc = XDocument.Load(rpsAddinXmlPath);

                // create subfolder
                _outputFolder = CreateOutputFolder();

                // copy static stuff (rpsaddin runtime, ironpython dlls etc., addin installation utilities)
                CopyFile(typeof(RpsExternalApplicationBase).Assembly.Location);          // RpsRuntime.dll

                var ironPythonPath = Path.GetDirectoryName(this.GetType().Assembly.Location);
                CopyFile(Path.Combine(ironPythonPath, "IronPython.dll"));                    // IronPython.dll
                CopyFile(Path.Combine(ironPythonPath, "IronPython.Modules.dll"));            // IronPython.Modules.dll
                CopyFile(Path.Combine(ironPythonPath, "Microsoft.Scripting.dll"));           // Microsoft.Scripting.dll
                CopyFile(Path.Combine(ironPythonPath, "Microsoft.Scripting.Metadata.dll"));  // Microsoft.Scripting.Metadata.dll
                CopyFile(Path.Combine(ironPythonPath, "Microsoft.Dynamic.dll"));             // Microsoft.Dynamic.dll

                // Copy Roslyn assemblies if they exist (needed for the compiled assembly)
                var roslynAssemblies = new[] {
                    "Microsoft.CodeAnalysis.dll",
                    "Microsoft.CodeAnalysis.CSharp.dll",
                    "System.Collections.Immutable.dll",
                    "System.Reflection.Metadata.dll"
                };

                foreach (var roslynAssembly in roslynAssemblies)
                {
                    var roslynPath = Path.Combine(ironPythonPath, roslynAssembly);
                    if (File.Exists(roslynPath))
                    {
                        CopyFile(roslynPath);
                    }
                }

                // copy files mentioned (they must all be unique)
                CopyIcons();

                CopyExplicitFiles();

                // create addin assembly
                CreateAssembly();

                Autodesk.Revit.UI.TaskDialog.Show("Deploy RpsAddin", "Deployment complete - see folder: " + _outputFolder);

                return Result.Succeeded;
            }
            catch (Exception exception)
            {

                Autodesk.Revit.UI.TaskDialog.Show("Deploy RpsAddin", "Error deploying addin: " + exception.ToString());
                return Result.Failed;
            }
        }

        /// <summary>
        /// Copy any icon files mentioned in PushButton tags. 
        /// 
        /// The PythonScript16x16.png and PythonScript32x32.png icons will be used as default,
        /// if no icons are found (they are embedded in the RpsRuntime.dll)
        /// 
        /// as always, relative paths are assumed to be relative to rootFolder, that
        /// is the folder that the RpsAddin xml file came from.
        /// </summary>
        private void CopyIcons()
        {
            HashSet<string> copiedIcons = new HashSet<string>();

            foreach (var pb in _doc.Descendants("PushButton"))
            {
                CopyReferencedFileToOutputFolder(pb.Attribute("largeImage"));
                CopyReferencedFileToOutputFolder(pb.Attribute("smallImage"));
            }
        }        

        /// <summary>
        /// Copy a file to the output folder ("flat" folder structure!)
        /// </summary>
        private void CopyFile(string path)
        {
            File.Copy(path, Path.Combine(_outputFolder, Path.GetFileName(path)));
        }

        /// <summary>
        /// Copy all files mentioned in /Files/File tags.
        /// </summary>
        private void CopyExplicitFiles()
        {
            foreach (var xmlFile in _doc.Descendants("Files").SelectMany(f => f.Descendants("File")))
            {
                var source = xmlFile.Attribute("src").Value;
                var sourcePath = GetRootedPath(_rootFolder, source);

                if (!File.Exists(sourcePath))
                {
                    throw new FileNotFoundException(
                        "Could not find the explicitly referenced file",
                        source);
                }

                var fileName = Path.GetFileName(sourcePath);
                File.Copy(sourcePath, Path.Combine(_outputFolder, fileName));

                // remove path information for deployment
                xmlFile.Attribute("src").Value = fileName;
            }
        }


        /// <summary>
        /// Copies a referenced file to the output folder, unless it could not find that
        /// file.
        /// </summary>
        private void CopyReferencedFileToOutputFolder(XAttribute attr)
        {
            if (attr == null)
            {
                return;
            }

            var path = GetRootedPath(_rootFolder, attr.Value);
            if (path != null)
            {
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException(
                        "Could not find the file referenced by attribute " + attr.Name,
                        attr.Value);
                }

                var fileName = Path.GetFileName(path);
                File.Copy(path, Path.Combine(_outputFolder, fileName));
                
                // make the new value relative, for the embedded RpsAddin xml
                attr.Value = fileName;
            }                           
        }

        /// <summary>
        /// Show a FileDialog for the RpsAddinXml file and return the path.
        /// </summary>
        private string GetAddinXmlPath()
        {
            var dialog = new OpenFileDialog();
            dialog.CheckFileExists = true;
            dialog.CheckPathExists = true;
            dialog.Multiselect = false;
            dialog.DefaultExt = "xml";
            dialog.Filter = "RpsAddin xml files (*.xml)|*.xml";

            dialog.ShowDialog();
            return dialog.FileName;
        }

        /// <summary>
        /// Create a new dll Assembly in the outputFolder with the addinName and
        /// add the RpsAddin xml file and all script files referenced by PushButton tags
        /// as embedded resources, plus, for each such script, add a subclass of
        /// RpsExternalCommand to load the script from.    
        /// </summary>

        private void CreateAssembly()
        {
            // Generate C# source code for the addin
            var sourceCode = GenerateAddinSourceCode();
            
            // Get all Python scripts to embed as resources
            var scriptResources = new Dictionary<string, string>();
            foreach (var xmlPushButton in _doc.Descendants("PushButton"))
            {
                string scriptFileName = xmlPushButton.Attribute("src")?.Value ?? xmlPushButton.Attribute("script")?.Value;
                if (scriptFileName == null)
                {
                    throw new ApplicationException("<PushButton/> tag missing a src attribute in addin manifest");
                }

                var scriptFile = GetRootedPath(_rootFolder, scriptFileName);
                if (!File.Exists(scriptFile))
                {
                    throw new FileNotFoundException("Could not find script file", scriptFile);
                }

                var newScriptFile = Path.GetFileName(scriptFile);
                scriptResources[newScriptFile] = File.ReadAllText(scriptFile);
                
                // Update XML to point to the new filename
                xmlPushButton.Attribute("src").Value = newScriptFile;
            }
            
            // Add RpsAddin xml as resource
            var xmlContent = _doc.ToString();
            
            // Compile with Roslyn and save to disk
            CompileWithRoslyn(sourceCode, scriptResources, xmlContent);
        }

        private string GenerateAddinSourceCode()
        {
            var sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using Autodesk.Revit.Attributes;");
            sb.AppendLine("using Autodesk.Revit.DB;");
            sb.AppendLine("using Autodesk.Revit.UI;");
            sb.AppendLine("using RpsRuntime;");
            sb.AppendLine();

            // Generate the main application class (no namespace - Revit looks for class by simple name)
            sb.AppendLine($"[Transaction(TransactionMode.Manual)]");
            sb.AppendLine($"[Regeneration(RegenerationOption.Manual)]");
            sb.AppendLine($"public class {_addinName} : RpsExternalApplicationBase");
            sb.AppendLine("{");
            sb.AppendLine("    // The base class handles everything automatically");
            sb.AppendLine("}");
            sb.AppendLine();

            // Generate IExternalCommand classes for each PushButton (no namespace)
            foreach (var xmlPushButton in _doc.Descendants("PushButton"))
            {
                string scriptFileName = xmlPushButton.Attribute("src")?.Value ?? xmlPushButton.Attribute("script")?.Value;
                if (scriptFileName != null)
                {
                    var scriptName = Path.GetFileNameWithoutExtension(scriptFileName);
                    var className = "ec_" + scriptName;

                    sb.AppendLine($"[Transaction(TransactionMode.Manual)]");
                    sb.AppendLine($"[Regeneration(RegenerationOption.Manual)]");
                    sb.AppendLine($"public class {className} : RpsExternalCommandBase");
                    sb.AppendLine("{");
                    sb.AppendLine($"    public {className}() : base(\"{scriptFileName}\") {{ }}");
                    sb.AppendLine("}");
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        private void CompileWithRoslyn(string sourceCode, Dictionary<string, string> scriptResources, string xmlContent)
        {
            // Parse the source code
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);

            // Get reference assemblies
            var references = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Autodesk.Revit.Attributes.TransactionAttribute).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Autodesk.Revit.UI.IExternalCommand).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Autodesk.Revit.DB.Document).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(RpsRuntime.RpsExternalApplicationBase).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Microsoft.CSharp.RuntimeBinder.Binder).Assembly.Location),
            };

            // Add System.Runtime and other core references
            var systemRuntimeAssembly = Assembly.Load("System.Runtime");
            if (systemRuntimeAssembly != null)
            {
                references.Add(MetadataReference.CreateFromFile(systemRuntimeAssembly.Location));
            }

            // Add netstandard if available (needed for .NET Framework compatibility)
            try
            {
                var netstandardAssembly = Assembly.Load("netstandard");
                if (netstandardAssembly != null)
                {
                    references.Add(MetadataReference.CreateFromFile(netstandardAssembly.Location));
                }
            }
            catch
            {
                // netstandard not available, continue without it
            }

            // Add IronPython references
            var ironPythonPath = Path.GetDirectoryName(typeof(Microsoft.Scripting.Hosting.ScriptEngine).Assembly.Location);
            if (Directory.Exists(ironPythonPath))
            {
                references.Add(MetadataReference.CreateFromFile(Path.Combine(ironPythonPath, "IronPython.dll")));
                references.Add(MetadataReference.CreateFromFile(Path.Combine(ironPythonPath, "IronPython.Modules.dll")));
                references.Add(MetadataReference.CreateFromFile(Path.Combine(ironPythonPath, "Microsoft.Dynamic.dll")));
                references.Add(MetadataReference.CreateFromFile(Path.Combine(ironPythonPath, "Microsoft.Scripting.dll")));
                references.Add(MetadataReference.CreateFromFile(Path.Combine(ironPythonPath, "Microsoft.Scripting.Metadata.dll")));
            }

            // Prepare embedded resources
            // Store byte arrays to avoid closure issues with lambdas
            var resourceData = new Dictionary<string, byte[]>();

            // Add XML manifest
            var xmlResourceName = $"{_addinName}.xml";
            resourceData[xmlResourceName] = Encoding.UTF8.GetBytes(xmlContent);

            // Add Python scripts
            foreach (var script in scriptResources)
            {
                resourceData[script.Key] = Encoding.UTF8.GetBytes(script.Value);
            }

            // Create ResourceDescription list with proper lambda closures
            var manifestResources = new List<ResourceDescription>();
            foreach (var kvp in resourceData)
            {
                var name = kvp.Key;
                var data = kvp.Key;
                manifestResources.Add(new ResourceDescription(
                    name,
                    () => new MemoryStream(resourceData[data]),
                    isPublic: true
                ));
            }

            // Create compilation
            var compilation = CSharpCompilation.Create(
                $"{_addinName}.dll",
                syntaxTrees: new[] { syntaxTree },
                references: references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );

            // Create output stream for the DLL
            var outputPath = Path.Combine(_outputFolder, $"{_addinName}.dll");

            // Emit the compilation WITHOUT resources first (Roslyn's ResourceDescription doesn't work reliably)
            using (var dllStream = File.Create(outputPath))
            {
                var emitResult = compilation.Emit(peStream: dllStream);

                if (!emitResult.Success)
                {
                    // Handle compilation errors
                    var errors = new StringBuilder();
                    errors.AppendLine("Compilation failed:");
                    foreach (var diagnostic in emitResult.Diagnostics)
                    {
                        if (diagnostic.IsWarningAsError || diagnostic.Severity == DiagnosticSeverity.Error)
                        {
                            errors.AppendLine(diagnostic.ToString());
                        }
                    }
                    throw new ApplicationException(errors.ToString());
                }
            }

            // Save resources as separate files since Roslyn embedding doesn't work
            // The RpsRuntime code will need to be modified to load from files instead
            foreach (var kvp in resourceData)
            {
                var resourcePath = Path.Combine(_outputFolder, kvp.Key);
                File.WriteAllBytes(resourcePath, kvp.Value);
            }
        }



        /// <summary>
        /// Returns the possiblyRelativePath rooted in sourceFolder,
        /// if it is relative or unchanged if it is absolute already.
        /// if the input is null or an empty string, returns null.
        /// </summary>
        private static string GetRootedPath(string sourceFolder, string possiblyRelativePath)
        {
            if (string.IsNullOrEmpty(possiblyRelativePath))
            {
                return null;
            }

            if (!Path.IsPathRooted(possiblyRelativePath))
            {
                return Path.Combine(sourceFolder, possiblyRelativePath);
            }
            return possiblyRelativePath;
        }

        /// <summary>
        /// Adds a subclass of RpsExternalApplicationBase to make the assembly
        /// work as an external application.
        /// </summary>
        private void AddExternalApplicationToAssembly(string addinName, ModuleBuilder moduleBuilder)
        {
            var typeBuilder = moduleBuilder.DefineType(
                addinName,
                TypeAttributes.Class | TypeAttributes.Public,
                typeof(RpsExternalApplicationBase));
            AddRegenerationAttributeToType(typeBuilder);
            AddTransactionAttributeToType(typeBuilder);
            typeBuilder.CreateType();
        }

        /// <summary>
        /// Adds the [Transaction(TransactionMode.Manual)] attribute to the type.        
        /// </summary>
        private void AddTransactionAttributeToType(TypeBuilder typeBuilder)
        {
            var transactionConstructorInfo = typeof(TransactionAttribute).GetConstructor(new Type[] { typeof(TransactionMode) });
            var transactionAttributeBuilder = new CustomAttributeBuilder(transactionConstructorInfo, new object[] { TransactionMode.Manual });
            typeBuilder.SetCustomAttribute(transactionAttributeBuilder);
        }

        /// <summary>
        /// Adds the [Transaction(TransactionMode.Manual)] attribute to the type.
        /// </summary>
        /// <param name="typeBuilder"></param>
        private void AddRegenerationAttributeToType(TypeBuilder typeBuilder)
        {
            var regenerationConstrutorInfo = typeof(RegenerationAttribute).GetConstructor(new Type[] { typeof(RegenerationOption) });
            var regenerationAttributeBuilder = new CustomAttributeBuilder(regenerationConstrutorInfo, new object[] { RegenerationOption.Manual });
            typeBuilder.SetCustomAttribute(regenerationAttributeBuilder);
        }

        private void AddRpsAddinXmlToAssembly(string addinName, XDocument doc, ModuleBuilder moduleBuilder)
        {
            var stream = new MemoryStream();
            var writer = new StreamWriter(stream);
            writer.Write(doc.ToString());
            writer.Flush();
            stream.Position = 0;

#if !NET8_0
            moduleBuilder.DefineManifestResource(addinName + ".xml", stream, ResourceAttributes.Public);
#else
            var resourceFilePath = Path.Combine(_outputFolder, $"{_addinName}.resources");
            using (var resWriter = new ResourceWriter(resourceFilePath))
            {
                resWriter.AddResource(addinName + ".xml", stream);
                resWriter.Generate();
            }
#endif
        }

        /// <summary>
        /// Creates a subfolder in rootFolder with the basename of the
        /// RpsAddin xml file and returns the name of that folder.
        /// 
        /// deletes previous folders.
        /// 
        /// result: "Output_HelloWorld"
        /// </summary>
        private string CreateOutputFolder()
        {
            var folderName = $"Output_{_addinName}";
            var folderPath = Path.Combine(_rootFolder, folderName);
            if (Directory.Exists(folderPath))
            {
                Directory.Delete(folderPath, true);
            }
#if !NET8_0
            Directory.CreateDirectory(folderPath, Directory.GetAccessControl(_rootFolder));
#else
            Directory.CreateDirectory(folderPath);
#endif
            return folderPath;
        }
    }
}
