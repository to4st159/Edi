﻿
using System.Text.Json;
using System.Globalization;
using CsvHelper;
using File = System.IO.File;
using Edi.Core.Funscript;

using System.Runtime.CompilerServices;
using Edi.Core.Gallery.Definition;
using System.Text.RegularExpressions;
using System.Linq;
using System.Collections.Generic;

namespace Edi.Core.Gallery.CmdLineal
{
    public class FunscriptRepository : IGalleryRepository<FunscriptGallery>
    {
        public FunscriptRepository(ConfigurationManager configuration, DefinitionRepository definition)
        {
            Config = configuration.Get<GalleryConfig>();
            Definition = definition;
        }
        private Dictionary<string, List<FunscriptGallery>> Galleries { get; set; } = new Dictionary<string, List<FunscriptGallery>>(StringComparer.OrdinalIgnoreCase);


        private List<string> Variants { get; set; } = new List<string>();
        public  GalleryConfig Config { get; set; }
        public DefinitionRepository Definition { get; }

        public async Task Init()
        {
            LoadFromFunscripts();
        }


        private void LoadFromFunscripts()
        {
            var GalleryPath = $"{Config.GalleryPath}\\";
            Galleries.Clear();
            Variants.Clear();
            if (!Directory.Exists($"{GalleryPath}"))
                return;

            var GalleryDir = new DirectoryInfo(Config.GalleryPath);

            var FilesSourceNames = Definition
                                    .GetAll().Select(x => x.FileName)
                                    .Distinct().ToHashSet(StringComparer.InvariantCultureIgnoreCase);

            var funscriptsFiles = GetFunscripts()
                                    .Where(x=> FilesSourceNames.Contains(x.name))
                                    .ToList();

            foreach (var funscript in funscriptsFiles)
            {
                var pathVariant = funscript.path.Replace(GalleryPath, "").Split('\\')[0];
                funscript.variant = !string.IsNullOrEmpty(funscript.variant)
                                        ? funscript.variant
                                        : pathVariant ?? Config.DefaulVariant;
            }
            
            foreach (var DefinitionGallery in Definition.GetAll())
            {
                Galleries.Add(DefinitionGallery.Name, new List<FunscriptGallery>());

                var funscripts = funscriptsFiles
                                        .Where(x => x.name == DefinitionGallery.FileName)
                                        .DistinctBy(x=> x.variant);

                foreach (var funscript in funscripts)
                {
                    var actions = funscript.actions
                        .Where(x => x.at > DefinitionGallery.StartTime
                                 && x.at <= DefinitionGallery.EndTime);

                    FunscriptGallery gallery = ParseActions(funscript.variant, DefinitionGallery, actions);

                    Galleries[DefinitionGallery.Name].Add(gallery);
                }
            }
            Variants = Galleries.SelectMany(x => x.Value.Select(y => y.Variant)).Distinct().ToList();
        }

        public List<FunScriptFile> GetFunscripts()
        {
            var GalleryDir = new DirectoryInfo(Config.GalleryPath);
            

            var funscriptsFiles = GalleryDir.EnumerateFiles("*.funscript").ToList();
            funscriptsFiles.AddRange(GalleryDir.EnumerateDirectories().SelectMany(d => d.EnumerateFiles("*.funscript")));

            return  funscriptsFiles
                        .Select(x => FunScriptFile.TryRead(x.FullName))
                        .Where(x=> x != null && x.actions?.Any() == true) 
                        .ToList();
        }

        private Dictionary<string, FunScriptFile> GetGalleryFunscripts()
        {
            var FunscriptCache = new Dictionary<string, FunScriptFile>(StringComparer.OrdinalIgnoreCase);
            foreach (var variantPath in Variants)
            {
                var variant = new DirectoryInfo(variantPath).Name;
                foreach (var DefinitionGallery in Definition.GetAll())
                {
                    var filePath = $"{Config.GalleryPath}\\{variant}\\{DefinitionGallery.FileName}.funscript";

                    if(!File.Exists(filePath))
                        filePath = $"{Config.GalleryPath}\\{variant}\\{DefinitionGallery.FileName}.{variant}.funscript";

                    FunScriptFile funscript;
                    if (!FunscriptCache.ContainsKey(filePath))
                    {
                        if (!File.Exists(filePath))
                            continue;
                        try
                        {
                            funscript = JsonSerializer.Deserialize<FunScriptFile>(File.ReadAllText(filePath));
                            funscript.actions = funscript.actions.OrderBy(x => x.at).ToList();
                        }
                        catch
                        {
                            continue;
                        }
                        FunscriptCache.Add(filePath, funscript);
                    }
                }
            }
            return FunscriptCache;

        }

        private static FunscriptGallery ParseActions(string variant, DefinitionGallery DefinitionGallery, IEnumerable<FunScriptAction> actions)
        {
            var sb = new ScriptBuilder();
            foreach (var action in actions)
            {
                sb.AddCommandMillis(
                    millis: Convert.ToInt32(action.at - DefinitionGallery.StartTime - sb.TotalTime),
                    value: action.pos);
            }
            var gallery = new FunscriptGallery
            {
                Name = DefinitionGallery.Name,
                Variant = variant,
                Loop = DefinitionGallery.Loop,
                //Duration = DefinitionGallery.Duration,

            };
            sb.TrimTimeTo(DefinitionGallery.Duration);

            gallery.Commands = sb.Generate();

            return gallery;
        }

        public List<string> GetVariants()
            => Variants;
        public List<FunscriptGallery> GetAll()
            => Galleries.Values.SelectMany(x => x).ToList();

        public FunscriptGallery? Get(string name, string variant = null)
        {
            //TODO: asset ovverride order priority similar minecraft texture packt 
            variant = variant ?? Config.SelectedVariant ?? Config.DefaulVariant;

            var variants = Galleries.GetValueOrDefault(name);

            if (variants is null)
                return null;

            var gallery = variants.FirstOrDefault(x => x.Variant == variant)
                        ?? variants.FirstOrDefault(x => x.Variant == Config.SelectedVariant)
                        ?? variants.FirstOrDefault();
            return gallery;

        }




        /*
        private void LoadGalleryFromCsv()
        {
            var GalleryPath = $"{Config.GalleryPath}\\";

            if (!Directory.Exists($"{GalleryPath}"))
                return;

            var variants = Directory.GetDirectories($"{GalleryPath}");

            if (!variants.Any())
            {
                //variants = GetVarinasFromFunscripts();
            }

            Variants.Clear();
            Variants.AddRange(variants);

            var FunscriptCache = GetGalleryFunscripts();
            Galleries.Clear();
            foreach (var variantPath in variants)
            {
                var variantDir = new DirectoryInfo(variantPath);
                string variant = variantDir.Name;
                foreach (var DefinitionGallery in Definition.GetAll())
                {
                    string filePath;
                    if (!variantDir.Exists)
                    {
                        filePath = $"{Config.GalleryPath}\\{variant}\\{DefinitionGallery.FileName}.funscript";

                        if (!FunscriptCache.ContainsKey(filePath))
                            filePath = $"{Config.GalleryPath}\\{variant}\\{DefinitionGallery.FileName}.{variant}.funscript";
                    }
                    else
                    {
                        filePath = $"{Config.GalleryPath}\\{DefinitionGallery.FileName}.{variant}.funscript";
                    }

                    if (!FunscriptCache.ContainsKey(filePath))
                        continue;

                    var funscript = FunscriptCache[filePath];

                    var actions = funscript.actions
                        .Where(x => x.at > DefinitionGallery.StartTime
                                 && x.at <= DefinitionGallery.EndTime);

                    if (!actions.Any())
                        continue;


                    FunscriptGallery gallery = ParseActions(variant, DefinitionGallery, actions);

                    if (!Galleries.ContainsKey(DefinitionGallery.Name))
                        Galleries.Add(DefinitionGallery.Name, new List<FunscriptGallery>());

                    Galleries[gallery.Name].Add(gallery);
                }
            }
            Variants = Galleries.SelectMany(x => x.Value.Select(y => y.Variant)).Distinct().ToList();
        }

        */
    }
}
