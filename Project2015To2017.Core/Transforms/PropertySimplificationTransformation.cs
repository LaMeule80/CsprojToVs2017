using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Project2015To2017.Definition;
using Project2015To2017.Reading;

namespace Project2015To2017.Transforms
{
	public sealed class PropertySimplificationTransformation : ITransformation
	{
		private static readonly string[] IgnoreProjectNameValues =
		{
			"$(MSBuildProjectName)",
			"$(ProjectName)"
		};

		private readonly Version targetVisualStudioVersion;

		public PropertySimplificationTransformation(Version targetVisualStudioVersion = null)
		{
			this.targetVisualStudioVersion = targetVisualStudioVersion ?? new Version(15, 0);
		}

		public void Transform(Project definition)
		{
			var msbuildProjectName = definition.FilePath?.Name;
			if (!string.IsNullOrEmpty(msbuildProjectName))
			{
				msbuildProjectName = Path.GetFileNameWithoutExtension(msbuildProjectName);
			}
			else
			{
				// That's actually not what MSBuild does, but it is done to simplify tests
				// and has a incredibly low probability of being triggered on real projects
				// (no project file? empty project filename? seriously?)
				msbuildProjectName = definition.ProjectName;
			}

			// special case handling for when condition-guarded props override global props not set to their defaults
			var globalOverrides = new Dictionary<string, string>();
			foreach (var group in definition.UnconditionalGroups())
			{
				RetrieveGlobalOverrides(group, globalOverrides);
			}

			if (string.IsNullOrEmpty(definition.ProjectName))
			{
				if (!globalOverrides.TryGetValue("ProjectName", out var projectName))
					if (!globalOverrides.TryGetValue("AssemblyName", out projectName))
						projectName = msbuildProjectName;
				definition.ProjectName = projectName;
			}

			foreach (var propertyGroup in definition.PropertyGroups)
			{
				FilterUnneededProperties(definition, propertyGroup, globalOverrides, msbuildProjectName);
			}
		}

		private void FilterUnneededProperties(Project project,
			XElement propertyGroup,
			IDictionary<string, string> globalOverrides,
			string msbuildProjectName)
		{


			var removeQueue = new List<XElement>();

			List<string> items = new List<string>();
			items.Add("SccProjectName");
			items.Add("SccLocalPath");
			items.Add("SccAuxPath");
			items.Add("SccProvider");

			foreach (var child in propertyGroup.Elements())
			{
				var tagLocalName = child.Name.LocalName;

				if (tagLocalName == "ProjectTypeGuids")
				{
					var value = child.Value;
					var values = value.Split(';').Select(x => Guid.Parse(x));
					project.IsWPF = values.Contains(ProjectExtensions.WpfGuid);
				}

				if (!items.Contains(tagLocalName))
					removeQueue.Add(child);
			}

			// we cannot remove elements correctly while iterating through elements, 2nd pass is needed
			foreach (var child in removeQueue)
			{
				child.Remove();
			}
		}

		/// <summary>
		/// Get all non-conditional properties and their respective values
		/// </summary>
		/// <param name="propertyGroup">Primary unconditional PropertyGroup to be inspected</param>
		/// <param name="globalOverrides"></param>
		/// <returns>Dictionary of properties' keys and values</returns>
		private static void RetrieveGlobalOverrides(XElement propertyGroup, IDictionary<string, string> globalOverrides)
		{
			foreach (var child in propertyGroup.Elements())
			{
				if (!HasEmptyCondition(child))
				{
					continue;
				}

				globalOverrides[child.Name.LocalName] = child.Value.Trim();
			}

			bool HasEmptyCondition(XElement element)
			{
				var conditionAttribute = element.Attribute("Condition");
				if (conditionAttribute == null)
				{
					return true;
				}

				var condition = conditionAttribute.Value.Trim();

				// no sane condition is 1 char long
				return condition.Length <= 1;
			}
		}

		private static bool ValidateDefaultConstants(string value, params string[] expected)
		{
			var defines = value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
			return Extensions.ValidateSet(defines, expected);
		}
	}
}