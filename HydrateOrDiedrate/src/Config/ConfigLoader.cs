using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Vintagestory.API.Common;

namespace HydrateOrDiedrate.src.Config
{
    public static class ConfigLoader
    {
        public static JObject[] LoadPatches(ICoreAPI api, string configName, string patchKey = "itemname", Func<JObject> defaultConfigGenerator = null)
        {
            JObject userConfig = api.LoadModConfig<JObject>(configName);
            JObject defaultConfig = defaultConfigGenerator?.Invoke();

            if (userConfig is null) userConfig = defaultConfig;
            else if(defaultConfig is not null) DeepMerge(defaultConfig, userConfig, patchKey);

            if(userConfig is null) return Array.Empty<JObject>();

            api.StoreModConfig(userConfig, configName);


            if(userConfig["patches"] is not JArray patches) return Array.Empty<JObject>();

            return patches.OfType<JObject>()
                .Where(item => item[patchKey] is JValue name && name.Type == JTokenType.String && !string.IsNullOrEmpty(name.Value<string>()))
                .ToArray();

        }

        private static void DeepMerge(JObject source, JObject target, string patchKey = "itemname")
        {
            foreach (var prop in source.Properties())
            {
                if(!target.TryGetValue(prop.Name, out var targetProp))
                {
                    target[prop.Name] = prop.Value; //QUESTION is cloning really needed here?
                    continue;
                }
                
                if (prop.Name == "patches" && prop.Value is JArray sourceArr && targetProp is JArray targetArr)
                {
                    foreach (var sourceItem in sourceArr.OfType<JObject>())
                    {
                        var itemNameProp = sourceItem[patchKey];
                        if (itemNameProp.Type != JTokenType.String || string.IsNullOrEmpty(itemNameProp.Value<string>()))
                            continue;

                        var targetItem = targetArr.OfType<JObject>()
                            .FirstOrDefault(x => itemNameProp.Equals(x[patchKey] as JValue));
                        
                        if (targetItem == null)
                        {
                            targetArr.Add(sourceItem); //QUESTION is cloning really needed here?
                        }
                        else DeepMerge(sourceItem, targetItem);
                    }
                }
                else if (prop.Value is JObject sourceObj && targetProp is JObject targetObj)
                {
                    DeepMerge(sourceObj, targetObj);
                }
                else if (prop.Value is JArray sourceArray && targetProp is JArray targetArray)
                {
                    foreach (var item in sourceArray)
                    {
                        if (!targetArray.Any(t => JToken.DeepEquals(t, item)))
                        {
                            targetArray.Add(item); //QUESTION is cloning really needed here?
                        }
                    }
                }
            }
        }
    }
}
