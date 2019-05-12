﻿using System;

namespace PackagesConfigProjectConverter
{
    public static class ProjectConverterFactory
    {
        internal static Func<ProjectConverterSettings, IProjectConverter> Creator { get; set; } = settings => new ProjectConverter(settings);

        public static IProjectConverter Create(ProjectConverterSettings settings)
        {
            switch (settings.Convertertype)
            {
                case Convertertype.CopyConverter:
                    return new FixCopyNewest(settings);
                case Convertertype.PackageConverter:
                    return new ProjectConverter(settings);
            }
            throw new SystemException("Unsupported converter");
        }
    }
}