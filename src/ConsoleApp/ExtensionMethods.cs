﻿using Microsoft.Build.Construction;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml;

namespace PackagesConfigProjectConverter
{
    internal static class ExtensionMethods
    {
        private static readonly string ParentDirectory = $"..{Path.DirectorySeparatorChar}";
        private static readonly PropertyInfo ProjectElementXmlPropertyInfo = typeof(ProjectElement).GetProperty("XmlElement", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly string ThisDirectory = $".{Path.DirectorySeparatorChar}";


        public static bool TryAdd<K,V>(this Dictionary<K,V> dictionary, K key, V val)
        {
            if (!dictionary.ContainsKey(key))
            {
                dictionary.Add(key, val);
                return true;
            }
            return false;
        }
        public static ProjectMetadataElement AddMetadataAsAttribute(this ProjectItemElement itemElement, string name, string unevaluatedValue)
        {
            return itemElement.AddMetadata(name, unevaluatedValue, expressAsAttribute: true);
        }

        public static ProjectItemElement AppendItem(this ProjectItemGroupElement itemGroupElement, string itemType, string include)
        {
            ProjectItemElement itemElement = itemGroupElement.ContainingProject.CreateItemElement(itemType, include);

            itemGroupElement.AppendChild(itemElement);

            return itemElement;
        }

        public static string GetIncludeFullPath(this ProjectItemElement itemElement)
        {
            return GetProjectFullPath(itemElement.ContainingProject, itemElement.Include);
        }

        public static string GetProjectFullPath(this ProjectRootElement project, string path)
        {
            if ((path.StartsWith(ParentDirectory) || path.StartsWith(ThisDirectory)))
            {
                int starIndex = path.IndexOf("*", StringComparison.Ordinal);
                string firstHalf = path, secondHalf = "";
                if (starIndex != -1)
                {
                    firstHalf = path.Substring(0, starIndex);
                    secondHalf = path.Substring(starIndex);
                }

                return Path.GetFullPath(Path.Combine(project.DirectoryPath, firstHalf)) + secondHalf;
            }

            return path;
        }


        public static string GetReferenceItemPath(this ProjectItemElement itemElement)
        {
            return GetProjectFullPath(itemElement.ContainingProject, itemElement.Metadata.Value("HintPath") ?? itemElement.Include);
        }

        public static void Remove(this ProjectElement element)
        {
            if (element.Parent != null)
            {
                ProjectElementContainer parent = element.Parent;

                parent.RemoveChild(element);

                if (parent.Count == 0)
                {
                    parent?.Parent.RemoveChild(parent);
                }
            }
        }

        public static ProjectMetadataElement SetMetadata(this ProjectItemElement itemElement, string name, string unevaluatedValue)
        {
            ProjectMetadataElement metadataElement = itemElement.Metadata.FirstOrDefault(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (metadataElement != null)
            {
                metadataElement.Value = unevaluatedValue;
            }
            else
            {
                metadataElement = AddMetadataAsAttribute(itemElement, name, unevaluatedValue);
            }

            return metadataElement;
        }

        public static Regex ToRegex(this string expression)
        {
            return string.IsNullOrWhiteSpace(expression) ? null : new Regex(expression, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        }

        public static string ToXmlString(this ProjectElement element)
        {
            XmlElement node = ProjectElementXmlPropertyInfo.GetValue(element) as XmlElement;

            return $"<{element.ElementName} {string.Join(" ", node.Attributes.Cast<XmlAttribute>().Select(i => $"{i.LocalName}=\"{i.Value}\""))}/>";
        }

        public static string Value(this ICollection<ProjectMetadataElement> metadata, string name)
        {
            return metadata.FirstOrDefault(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
        }
    }
}