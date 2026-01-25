using System.Collections.Generic;
using UnityEditor.PackageManager;

namespace UnityMCP.Editor.Resources.Package
{
    /// <summary>
    /// Resource provider for installed package information.
    /// </summary>
    public static class InstalledPackages
    {
        /// <summary>
        /// Gets a list of all installed packages in the project.
        /// </summary>
        /// <returns>Object containing installed package information.</returns>
        [MCPResource("packages://installed", "List of installed packages and their versions")]
        public static object GetInstalledPackages()
        {
            // Use the synchronous search for packages
            var listRequest = Client.List(offlineMode: false, includeIndirectDependencies: true);

            // Wait for the request to complete
            while (!listRequest.IsCompleted)
            {
                System.Threading.Thread.Sleep(10);
            }

            if (listRequest.Status == StatusCode.Failure)
            {
                return new
                {
                    error = true,
                    message = listRequest.Error?.message ?? "Failed to list packages",
                    packages = new object[0]
                };
            }

            var packagesList = new List<object>();
            var directDependencies = new List<object>();
            var indirectDependencies = new List<object>();

            foreach (var package in listRequest.Result)
            {
                var packageInfo = new
                {
                    name = package.name,
                    displayName = package.displayName,
                    version = package.version,
                    description = TruncateDescription(package.description),
                    source = package.source.ToString(),
                    status = package.status.ToString(),
                    isDirectDependency = package.isDirectDependency,
                    documentationUrl = package.documentationUrl,
                    changelogUrl = package.changelogUrl,
                    author = package.author?.name,
                    registry = package.registry?.name
                };

                packagesList.Add(packageInfo);

                if (package.isDirectDependency)
                {
                    directDependencies.Add(new
                    {
                        name = package.name,
                        version = package.version
                    });
                }
                else
                {
                    indirectDependencies.Add(new
                    {
                        name = package.name,
                        version = package.version
                    });
                }
            }

            return new
            {
                summary = new
                {
                    totalCount = packagesList.Count,
                    directDependencyCount = directDependencies.Count,
                    indirectDependencyCount = indirectDependencies.Count
                },
                directDependencies = directDependencies.ToArray(),
                indirectDependencies = indirectDependencies.ToArray(),
                packages = packagesList.ToArray()
            };
        }

        /// <summary>
        /// Truncates package description to avoid excessively long output.
        /// </summary>
        private static string TruncateDescription(string description)
        {
            if (string.IsNullOrEmpty(description))
            {
                return string.Empty;
            }

            const int maxLength = 200;
            if (description.Length <= maxLength)
            {
                return description;
            }

            return description.Substring(0, maxLength) + "...";
        }
    }
}
