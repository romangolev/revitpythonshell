using System;
using System.IO;
using System.Xml.Linq;
using Autodesk.Revit.UI;

namespace RpsRuntime
{
    /// <summary>
    /// An abstract base class for ExternalCommand instances created by the DeployRpsAddin projects.
    /// All that has to be done is subclass from this class - the script to run will
    /// be found inside the assembly embedded resources of the subclass.
    /// </summary>
    public abstract class RpsExternalCommandBase: IExternalCommand
    {

        /// <summary>
        /// Find the script in the resources and run it.
        /// </summary>
        private readonly string _scriptName;

        protected RpsExternalCommandBase(string scriptName)
        {
            _scriptName = scriptName;
        }

        Result IExternalCommand.Execute(ExternalCommandData commandData, ref string message, Autodesk.Revit.DB.ElementSet elements)
        {
            var executor = new ScriptExecutor(GetConfig(), commandData, message, elements);

            var assembly = this.GetType().Assembly;
            string source;

            // Try to load from embedded resource first
            using (var resourceStream = assembly.GetManifestResourceStream(_scriptName))
            {
                if (resourceStream != null)
                {
                    using (var reader = new StreamReader(resourceStream))
                    {
                        source = reader.ReadToEnd();
                    }
                }
                else
                {
                    // Fall back to file in assembly directory
                    var assemblyDir = Path.GetDirectoryName(assembly.Location);
                    var scriptPath = Path.Combine(assemblyDir, _scriptName);
                    if (File.Exists(scriptPath))
                    {
                        source = File.ReadAllText(scriptPath);
                    }
                    else
                    {
                        message = $"Could not find script '{_scriptName}' as embedded resource or file";
                        return Result.Failed;
                    }
                }
            }

            var result = executor.ExecuteScript(source, Path.Combine(assembly.Location, _scriptName));
            message = executor.Message;
            switch (result)
            {
                case (int)Result.Succeeded:
                    return Result.Succeeded;
                case (int)Result.Cancelled:
                    return Result.Cancelled;
                case (int)Result.Failed:
                    return Result.Failed;
                default:
                    return Result.Succeeded;
            }
        }

        /// <summary>
        /// Search for the config file first in the user preferences,
        /// then in the all users preferences.
        /// If not found, a new (empty) config file is created in the user preferences.
        /// </summary>
        private RpsConfig GetConfig()
        {
            var addinName = Path.GetFileNameWithoutExtension(this.GetType().Assembly.Location);
            var fileName =  addinName + ".xml";
            var userFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), addinName);
            
            var userFolderFile = Path.Combine(userFolder, fileName);
            if (File.Exists(userFolderFile))
            {
                return new RpsConfig(userFolderFile);
            }

            var allUserFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), addinName);
            var allUserFolderFile = Path.Combine(allUserFolder, addinName);
            if (File.Exists(allUserFolderFile))
            {
                return new RpsConfig(allUserFolderFile);
            }

            // create a new file in users appdata and return that
            var doc = new XDocument(
                new XElement("RevitPythonShell", 
                    new XElement("SearchPaths"),
                    new XElement("Variables")));

            if (!Directory.Exists(userFolder))
            {
                Directory.CreateDirectory(userFolder);
            }

            doc.Save(userFolderFile);
            return new RpsConfig(userFolderFile);
        }
    }
}
