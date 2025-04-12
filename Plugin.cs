using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Linq;
using RandomPaintingSwap;
using static AnotherRandomPaintingSwap.Plugin;

namespace AnotherRandomPaintingSwap;

[BepInPlugin("phnod.randompaintingswap", "Another Random Painting Swap", "1.0.3")]
public class Plugin : BaseUnityPlugin
{
    private const string IMAGE_LANDSCAPE_FOLDER_NAME = "RandomLandscapePaintingSwap_Images";
    private const string IMAGE_SQUARE_FOLDER_NAME    = "RandomSquarePaintingSwap_Images";
    private const string IMAGE_PORTRAIT_FOLDER_NAME  = "RandomPortraitPaintingSwap_Images";

    private const string MATERIAL_LANDSCAPE_ASSET_NAME = "GrungeHorizontalMaterial";
    private const string MATERIAL_PORTRAIT_ASSET_NAME  = "GrungeVerticalMaterial";

    static Material _LandscapeMaterial;
    static Material _PortraitMaterial;

    // These are 2x1
    public static readonly HashSet<string> whitelistLandscapeMaterials = new HashSet<string>
    {
        "Painting_H_Landscape",
        "Painting_H_crow",
        "Painting_H_crow_0",
        "PaintingMedium",
    };

    // Todo: filter all that begin with Painting_S_ as squares and so on?
    // Painting_S_Tree is actually a portrait though (Could be done with a blacklist)
    // Could possibly go through materials initially, grow hashmap of paintings and not paintings for faster sequential matches
    public static readonly HashSet<string> whitelistSquareMaterials = new HashSet<string>
    {
        "Painting_S_Creep",
        "Painting_S_Creep 2_0", // These paintings are sometimes stretched a little bit, about 1.1x taller than wide
        "Painting_S_Creep 2",
        "Painting Wizard Class",
    };

    // Most of these are 5x7 I think
    public static readonly HashSet<string> whitelistPortraitMaterials = new HashSet<string>
    {
        "Painting_V_jannk",
        "Painting_V_Furman",
        "Painting_V_surrealistic",
        "Painting_V_surrealistic_0",
        "painting teacher01",
        "painting teacher02",
        "painting teacher03",
        "painting teacher04",
        "Painting_S_Tree",
    };

    public class CustomPainting
    {
        public Material material;
        public string   textureName = "UNASSIGNED STRING";
    }

    public class ReplaceablePainting
    {
        public MeshRenderer meshRenderer;
    }

    // Defines groups of paintings depending on their dimensions, allowing to split landscape and portraits
    public class PaintingGroup
    {
        public string                    paintingType; // Dimension group (Landscape, Portrait)
        public string                    paintingFolderName; // The folder to search for this kind of painting
        public HashSet<string>           whitelistMaterials; // Which materials to replace
        public List<CustomPainting>      customPaintings; // Complete list of paintings
        public List<CustomPainting>      unusedPaintings; // Paintings that haven't been used yet
        public Material                  baseMaterial = null;

        public PaintingGroup(string InPaintingType,
                             string InPaintingFolderName,
                             HashSet<string> InWhitelistMaterials)
        {
            paintingType         = InPaintingType;
            paintingFolderName   = InPaintingFolderName;
            whitelistMaterials   = InWhitelistMaterials;
            customPaintings      = new List<CustomPainting>();
            unusedPaintings      = new List<CustomPainting>();
        }
    }

    // Found paintings that will be replaced with custom ones
    internal static List<ReplaceablePainting> replaceablePaintings = new List<ReplaceablePainting>();

    internal static int pseudorandomSeed = 0;

    public static List<PaintingGroup> paintingGroups;

    public static Plugin Instance { get; private set; }
    internal static new ManualLogSource Logger;

    static Plugin()
    {
        paintingGroups = new List<PaintingGroup>()
        {
            new PaintingGroup("Landscape", IMAGE_LANDSCAPE_FOLDER_NAME, whitelistLandscapeMaterials),
            new PaintingGroup("Square"   , IMAGE_SQUARE_FOLDER_NAME   , whitelistSquareMaterials),
            new PaintingGroup("Portrait" , IMAGE_PORTRAIT_FOLDER_NAME , whitelistPortraitMaterials),
        };
    }

    // File extensions
    public static readonly HashSet<string> imagePatterns = new HashSet<string>
    {
        "*.png",
        "*.jpg",
        "*.jpeg",
        "*.psd",
    };

    private readonly Harmony harmony = new Harmony("phnod.anotherrandompaintingswap");

    /**
     * Init Plugin
     */
    private void Awake()
    {
        Instance = this;

        // Plugin startup logic
        Logger = base.Logger;
        Logger.LogInfo($"Plugin Another Random Painting Swap is loaded!");

        PluginConfig.Init(Config);
        harmony.PatchAll(Assembly.GetExecutingAssembly());
        DebugLog($"DebugLog enabled. Expect bad loading performance");

        AssignMaterialGroups();

        LoadImagesFromAllPlugins();
    }

    // Get config values for the material
    private static void UpdateMaterialParameters()
    {
        foreach (var paintingGroup in paintingGroups)
        {
            var paintingType = paintingGroup.paintingType;
            if (paintingType == "Portrait")
            {
                paintingGroup.baseMaterial = _PortraitMaterial;
            }
            else // Square paintings can use the same material as landscape paintings
            {
                paintingGroup.baseMaterial = _LandscapeMaterial;
            }

            if (paintingGroup.baseMaterial == null)
            {
                Logger.LogWarning($"No base material found for [{paintingType}]!");
                continue;
            }

            if (PluginConfig.Grunge.enableGrunge.Value)
            {
                paintingGroup.baseMaterial.SetColor(PluginConfig.Grunge._BaseColor.Definition.Key   , PluginConfig.Grunge._BaseColor.Value);
                paintingGroup.baseMaterial.SetColor(PluginConfig.Grunge._MainColor.Definition.Key   , PluginConfig.Grunge._MainColor.Value);
                paintingGroup.baseMaterial.SetColor(PluginConfig.Grunge._CracksColor.Definition.Key , PluginConfig.Grunge._CracksColor.Value);
                paintingGroup.baseMaterial.SetColor(PluginConfig.Grunge._OutlineColor.Definition.Key, PluginConfig.Grunge._OutlineColor.Value);
                paintingGroup.baseMaterial.SetFloat(PluginConfig.Grunge._CracksPower.Definition.Key , PluginConfig.Grunge._CracksPower.Value);
            }
            else
            {
                paintingGroup.baseMaterial.SetColor(PluginConfig.Grunge._BaseColor.Definition.Key   , Color.clear);
                paintingGroup.baseMaterial.SetColor(PluginConfig.Grunge._MainColor.Definition.Key   , Color.clear);
                paintingGroup.baseMaterial.SetColor(PluginConfig.Grunge._CracksColor.Definition.Key , Color.clear);
                paintingGroup.baseMaterial.SetColor(PluginConfig.Grunge._OutlineColor.Definition.Key, Color.clear);
            }
        }
    }

    private void AssignMaterialGroups()
    {
        string location = Assembly.GetExecutingAssembly().Location;
        string directoryName = Path.GetDirectoryName(location);
        //string assetName = "AnotherRandomPaintingSwap";
        string assetName = "painting";
        string assetLocation = Path.Combine(directoryName, assetName);
        Logger.LogInfo($"Loading [{assetLocation}]");
        bool assetBundleExists = File.Exists(assetLocation);
        if (assetBundleExists)
        {
            Logger.LogInfo($"Asset Bundle exists");
        }
        else
        {
            Logger.LogWarning($"Asset Bundle doesn't exist");

        }

        AssetBundle assBundle = AssetBundle.LoadFromFile(assetLocation);

        if (assBundle == null)
        {
            Logger.LogError($"Failed to load [{assetName}]");
        }
        else
        {
            _LandscapeMaterial = assBundle.LoadAsset<Material>(MATERIAL_LANDSCAPE_ASSET_NAME);
            if (_LandscapeMaterial == null)
            {
                Logger.LogError($"Could not load landscape painting material [{MATERIAL_LANDSCAPE_ASSET_NAME}]!");
            }
            _PortraitMaterial = assBundle.LoadAsset<Material>(MATERIAL_PORTRAIT_ASSET_NAME);
            if (_PortraitMaterial == null)
            {
                Logger.LogError($"Could not load portrait painting material [{MATERIAL_PORTRAIT_ASSET_NAME}]!");
            }
        }

        UpdateMaterialParameters();
    }

    private void LoadImagesFromAllPlugins()
    {
        var pluginDir = Path.Combine(Paths.PluginPath);
        if (!Directory.Exists(pluginDir))
        {
            Logger.LogWarning($"Plugins directory not found: [{pluginDir}]");
            return;
        }

        foreach (var paintingGroup in paintingGroups)
        {
            var folderName = paintingGroup.paintingFolderName;
            string[] directories = Directory.GetDirectories(pluginDir, folderName, SearchOption.AllDirectories);
            if (directories.Length == 0)
            {
                Logger.LogWarning($"No 'CustomPaintings' folders found in plugins.");
                return;
            }
            string[] array = directories;
            foreach (string dirStr in array)
            {
                Logger.LogInfo($"Loading images from: [{dirStr}]");
                LoadImagesFromDirectory(paintingGroup, dirStr);
            }
        }
    }

    private void LoadImagesFromDirectory(PaintingGroup InPaintingGroup, string directoryPath)
    {
        string paintingType = InPaintingGroup.paintingType;

        if (!Directory.Exists(directoryPath))
        {
            Logger.LogWarning($"The folder [{directoryPath}] does not exist!");
            return;
        }

        Logger.LogInfo($"Selecting image patterns for group [{paintingType}] for files : {directoryPath}");
        List<string> imageFiles = imagePatterns.SelectMany(pattern => Directory.GetFiles(directoryPath, pattern)).ToList();

        if (!imageFiles.Any())
        {
            Logger.LogWarning($"No images found in the folder [{directoryPath}]");
            return;
        }

        foreach (var imageFile in imageFiles)
        {
            string filename = Path.GetFileName(imageFile);
            Texture2D texture = LoadTextureFromFile(imageFile);

            if (texture == null)
            {
                Logger.LogWarning($"Error loading image : [{imageFile}]");
                continue;
            }

            Material paintingGroupMaterial = InPaintingGroup.baseMaterial;
            Material material;
            if (paintingGroupMaterial == null)
            {
                material = new Material(Shader.Find("Standard")) { mainTexture = texture };
            }
            else
            {
                material = new Material(paintingGroupMaterial);
                material.SetTexture("_MainTex", texture);
            }

            var customPainting = new CustomPainting();
            customPainting.material = material;
            customPainting.textureName = filename;

            InPaintingGroup.customPaintings.Add(customPainting);

            Logger.LogInfo($"Created Material for group [{paintingType}] for loaded image : {filename}");
        }

        InPaintingGroup.unusedPaintings.Clear();
        InPaintingGroup.unusedPaintings.AddRange(InPaintingGroup.customPaintings);

        Logger.LogInfo($"Total Images for group [{paintingType}] : [{imageFiles.Count}]");
    }


    static int HashRoundedPosition(Vector3 position)
    {
        unchecked // Allows overflow to wrap around
        {
            int hash = 17;
            hash = hash * 23 + ((int)(position.x*10)).GetHashCode();
            hash = hash * 23 + ((int)(position.y*10)).GetHashCode();
            hash = hash * 23 + ((int)(position.z*10)).GetHashCode();
            return hash;
        }
    }

    static void PseudorandomSortList(List<ReplaceablePainting> InList)
    {
        Logger.LogDebug($"Randomly sorting painting list");

        if (InList == null)
        { 
            
            return; }

        if (InList.Count <= 0)
        { return; }

        pseudorandomSeed = 17;
        unchecked
        {
            foreach (var painting in InList)
            {
                var meshRenderer = painting.meshRenderer;
                pseudorandomSeed = pseudorandomSeed * 23 + HashRoundedPosition(meshRenderer.transform.position);
            }
        }
        // TODO: use the pseudorandomSeed to sort the paintings

        // Okay actually reorder the array here

        InList.Sort((a, b) =>
        {
            int zComparison = b.meshRenderer.transform.position.z.CompareTo(a.meshRenderer.transform.position.z);
            if (zComparison != 0)
            { return zComparison; }

            int xComparison = b.meshRenderer.transform.position.x.CompareTo(a.meshRenderer.transform.position.x);
            if (xComparison != 0)
            { return xComparison; }

            return b.meshRenderer.transform.position.y.CompareTo(a.meshRenderer.transform.position.y);
        });
    }

    static CustomPainting GetPseudorandomPainting(PaintingGroup InPaintingGroup, MeshRenderer InMeshRenderer, out int OutHash)
    {
        OutHash = 0;
        if (InPaintingGroup == null)
        {
            Logger.LogError($"Painting Group is NULL");
            return null;
        }

        if (InMeshRenderer == null)
        {
            Logger.LogError($"InMeshRenderer is NULL");
            return null;
        }

        if (InPaintingGroup.customPaintings == null)
        {
            Logger.LogError($"Painting Group custompaintings is NULL");
            return null;
        }

        if (InPaintingGroup.unusedPaintings == null)
        {
            Logger.LogError($"Painting Group unusedPaintings is NULL");
            return null;
        }

        // Refresh list if empty
        // This will ensure that every painting gets used before one is reused
        if (InPaintingGroup.unusedPaintings.Count <= 0)
        {
            InPaintingGroup.unusedPaintings.AddRange(InPaintingGroup.customPaintings);
            Logger.LogInfo($"Added all possible custom paintings for [{InPaintingGroup.paintingType}], adding new set of duplicates.");
        }

        if (InPaintingGroup.unusedPaintings.Count <= 0)
        { return null; }

        // Get pseudorandom value from the position of the mesh
        OutHash = Mathf.Abs(HashRoundedPosition(InMeshRenderer.transform.position));
        var index = OutHash % InPaintingGroup.unusedPaintings.Count;
        var painting = InPaintingGroup.unusedPaintings[index];
        InPaintingGroup.unusedPaintings.RemoveAt(index);
        return painting;
    }

    private Texture2D LoadTextureFromFile(string filePath)
    {
        byte[] fileData = File.ReadAllBytes(filePath);
        Texture2D texture = new Texture2D(2, 2);

        if (!texture.LoadImage(fileData))
        {
            texture = null; // clear texture
            return null;
        }

        texture.Apply();
        return texture;
    }

    private static void ReplaceMaterials()
    {
        PseudorandomSortList(replaceablePaintings);

        Logger.LogDebug("Replacing base images with plugin images");

        foreach (var replaceablePainting in replaceablePaintings)
        {
            var meshRenderer = replaceablePainting.meshRenderer;
            //foreach (var meshRenderer in gameObject.GetComponentsInChildren<MeshRenderer>())
            {
                var sharedMaterials = meshRenderer.sharedMaterials;

                if (sharedMaterials == null)
                {
                    continue;
                }

                for (int i = 0; i < sharedMaterials.Length; i++)
                {
                    foreach (var paintingGroup in paintingGroups)
                    {
                        var material = sharedMaterials[i];
                        if (material == null)
                        { continue; }

                        if (!paintingGroup.whitelistMaterials.Contains(material.name))
                        {
                            //DebugLog($"[{material.name}] does not contain whitelist match for [{paintingGroup.paintingType}].");
                            continue;
                        }
                        //DebugLog($"[{material.name}] does contain whitelist match for [{paintingGroup.paintingType}].");

                        if (paintingGroup.customPaintings.Count <= 0)
                        { continue; }

                        var selectedPainting = GetPseudorandomPainting(paintingGroup, meshRenderer, out int hash);

                        if (selectedPainting == null)
                        {
                            Logger.LogError($"Could not get painting from [{paintingGroup}][{meshRenderer}]");
                            continue; 
                        }

                        var rng = new System.Random(hash);
                        float rand = (float)rng.NextDouble();
                        float paintingChance = PluginConfig.customPaintingChance.Value;
                        if (rand > PluginConfig.customPaintingChance.Value)
                        {
                            Logger.LogInfo($"[{material.name}] will not be replaced by a [{paintingGroup.paintingType}]. Random Probability - [{rand}/{paintingChance}]");
                            continue;
                        }
                        //DebugLog($"[{material.name}] will be replaced by a [{paintingGroup.paintingType}].");

                        sharedMaterials[i] = selectedPainting.material;

                        Logger.LogInfo ($"Found ------------> [{material.name}] with texture [{material.mainTexture.name}]");
                        Logger.LogInfo ($"Converted to -----> [{selectedPainting.textureName}]");
                        var position = meshRenderer.transform.position;
                        var positionDebug = 
                            position.x.ToString("F1").PadLeft(7) + "," + 
                            position.y.ToString("F1").PadLeft(7) + "," +
                            position.z.ToString("F1").PadLeft(7);
                        Logger.LogDebug($"Located at -> [{positionDebug}]");
                    }
                }

                meshRenderer.sharedMaterials = sharedMaterials;
            }
        }
    }

    private static void GetReplacableMaterials(List<GameObject> InGameObjects)
    {
        Logger.LogInfo("Finding replaceable paintings");
        replaceablePaintings.Clear();

        foreach (var paintingGroup in paintingGroups)
        {
            paintingGroup.unusedPaintings.Clear();
            paintingGroup.unusedPaintings.AddRange(paintingGroup.customPaintings);
        }


        foreach (var gameObject in InGameObjects)
        {
            //DebugLog($"Checking game object [{gameObject.name}]");

            foreach (var meshRenderer in gameObject.GetComponentsInChildren<MeshRenderer>())
            {
                bool foundMatch = false;
                ReplaceablePainting replaceablePainting = null;
                var sharedMaterials = meshRenderer.sharedMaterials;

                if (sharedMaterials == null)
                {
                    continue;
                }

                for (int i = 0; i < sharedMaterials.Length; i++)
                {
                    foreach (var paintingGroup in paintingGroups)
                    {
                        var material = sharedMaterials[i];
                        if (material == null)
                        { continue; }

                        if (!paintingGroup.whitelistMaterials.Contains(material.name))
                        {
                            //DebugLog($"[{material.name}] does not contain whitelist match for [{paintingGroup.paintingType}].");
                            continue;
                        }
                        //DebugLog($"[{material.name}] does contain whitelist match for [{paintingGroup.paintingType}].");

                        if (paintingGroup.customPaintings.Count <= 0)
                        { continue; }

                        foundMatch = true;
                        replaceablePainting = new ReplaceablePainting { meshRenderer = meshRenderer };
                        
                    }
                }

                if (!foundMatch)
                { continue; }

                if (replaceablePainting == null)
                {
                    Logger.LogError($"Replaceable painting is null, this shouldn't be possible!");
                    continue;
                }

                replaceablePaintings.Add(replaceablePainting);
            }
        }
    }

    // Outdated, kept for reference
    private static void ReplaceWithCustomImages(List<GameObject> InGameObjects)
    {
        foreach (var gameObject in InGameObjects)
        {
            //DebugLog($"Checking game object [{gameObject.name}]");

            foreach (var meshRenderer in gameObject.GetComponentsInChildren<MeshRenderer>())
            {
                var sharedMaterials = meshRenderer.sharedMaterials;

                if (sharedMaterials == null)
                {
                    continue;
                }

                for (int i = 0; i < sharedMaterials.Length; i++)
                {
                    foreach (var paintingGroup in paintingGroups)
                    {
                        var material = sharedMaterials[i];
                        if (material == null)
                        { continue; }

                        if (!paintingGroup.whitelistMaterials.Contains(material.name))
                        {
                            //DebugLog($"[{material.name}] does not contain whitelist match for [{paintingGroup.paintingType}].");
                            continue;
                        }
                        //DebugLog($"[{material.name}] does contain whitelist match for [{paintingGroup.paintingType}].");

                        if (paintingGroup.customPaintings.Count <= 0)
                        { continue; }

                        var selectedPainting = GetPseudorandomPainting(paintingGroup, meshRenderer, out int hash);

                        float rand = UnityEngine.Random.Range(0.0f, 1.0f);
                        float paintingChance = PluginConfig.customPaintingChance.Value;
                        if (rand > PluginConfig.customPaintingChance.Value)
                        {
                            Logger.LogInfo($"[{material.name}] will not be replaced by a [{paintingGroup.paintingType}]. Random Probability - [{rand}/{paintingChance}]");
                            continue;
                        }
                        //DebugLog($"[{material.name}] will be replaced by a [{paintingGroup.paintingType}].");

                        var randomPaintingIndex = UnityEngine.Random.Range(0, paintingGroup.customPaintings.Count);
                        //var selectedPainting = paintingGroup.customPaintings[randomPaintingIndex];
                        sharedMaterials[i] = selectedPainting.material;

                        Logger.LogInfo($"Found ------------> [{material.name}] with texture [{material.mainTexture.name}]");
                        Logger.LogInfo($"Converted to -----> [{selectedPainting.textureName}]");
                    }
                }

                meshRenderer.sharedMaterials = sharedMaterials;
            }
        }
    }

    [HarmonyPatch(typeof(LoadingUI), "LevelAnimationComplete")]
    public class PatchLoadingUI
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            // Refresh painting list


            var activeScene = SceneManager.GetActiveScene();
            // All game objects
            var gameObjectList = activeScene.GetRootGameObjects().ToList();
            DebugLog($"Num of GameObjects: [{gameObjectList.Count}]");
            if ( gameObjectList.Count <= 0 )
            { return; }

            UpdateMaterialParameters();

            GetReplacableMaterials(gameObjectList);

            ReplaceMaterials();
        }
    }

    public static void DebugLog(string InMessage)
    {
        if (!PluginConfig.enableDebugLog.Value)
        { return; }

        Logger.LogDebug(InMessage);
    }
}