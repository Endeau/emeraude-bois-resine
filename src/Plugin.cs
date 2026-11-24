// Emeraude Bois résiné — une version résinée de chaque pièce de construction en bois simple, qui ne
// s'abîme plus sous la pluie ni dans l'eau. Même pièce, deux fois plus de bois. Les pentes, poutres en
// biais et toits de chaume, que la pluie n'use déjà pas, sont copiés aussi pour que tout ait la même teinte.
//
// Le jeu use une pièce mouillée de 5 % toutes les 60 s jusqu'à 50 % (WearNTear.UpdateWear) tant que
// m_noRoofWear est vrai. La copie résinée l'a à faux ; tout le reste (vie, support, matériau) est inchangé.
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace EmeraudeBoisResine
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "emeraude.boisresine";
        public const string Name = "Emeraude Bois Resine";
        public const string Version = "1.3.0";

        const string Suffix = "_resine";
        const string Category = "Resined wood";
        const int WoodFactor = 2;
        // Pièces 67° du bois simple, écartées par IsPlainWood : prises par leur nom.
        static readonly HashSet<string> Extra = new HashSet<string>
        {
            "wood_wall_roof_67_a", "wood_wall_roof_67_upsidedown", "wood_beam_67", "wood_wall_roof_top_67",
            "wood_roof_67", "wood_roof_icorner_67", "wood_roof_ocorner_67", "wood_roof_top_67",
        };
        // Ton miel plus sombre, multiplié à la couleur du bois.
        static readonly Color Amber = new Color(0.9f, 0.68f, 0.42f);

        void Awake()
        {
            AddTranslations();
            PrefabManager.OnVanillaPrefabsAvailable += AddResinedPieces;
            Logger.LogInfo($"{Name} {Version} loaded");
        }

        void AddTranslations()
        {
            var localization = LocalizationManager.Instance.GetLocalization();
            localization.AddTranslation("English", new Dictionary<string, string>
            {
                ["emeraude_resine"] = "resined",
                ["emeraude_resine_desc"] = "Doesn't wear out in the rain or in water.",
            });
            localization.AddTranslation("French", new Dictionary<string, string>
            {
                ["emeraude_resine"] = "résiné",
                ["emeraude_resine_desc"] = "Ne s'abîme pas sous la pluie ni dans l'eau.",
                // Jeton créé par Jötunn pour l'onglet, qui affiche le nom anglais par défaut.
                ["jotunn_cat_resined_wood"] = "Bois résiné",
            });
        }

        void AddResinedPieces()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= AddResinedPieces;
            var hammer = PrefabManager.Instance.GetPrefab("Hammer").GetComponent<ItemDrop>();
            var tinted = new Dictionary<Material, Material>();
            var added = new List<string>();
            var skipped = new List<string>();
            foreach (var original in hammer.m_itemData.m_shared.m_buildPieces.m_pieces.ToList())
            {
                if (!IsPlainWood(original) && !Extra.Contains(original.name))
                {
                    if (original.name.StartsWith("wood_")) skipped.Add(Describe(original));
                    continue;
                }
                var piece = new CustomPiece(original.name + Suffix, original.name, ConfigFrom(original.GetComponent<Piece>()));
                piece.PiecePrefab.GetComponent<WearNTear>().m_noRoofWear = false;
                Tint(piece.PiecePrefab, tinted);
                PieceManager.Instance.AddPiece(piece);
                added.Add(IsPlainWood(original) ? original.name : Describe(original));
            }
            Logger.LogInfo($"{added.Count} pièces résinées : {string.Join(", ", added)}");
            Logger.LogInfo($"{skipped.Count} pièces wood_ non résinées : {string.Join(" ; ", skipped)}");
        }

        // Pièce de l'onglet Construction qui ne coûte que du bois.
        static bool IsPlainWood(GameObject prefab)
        {
            var piece = prefab ? prefab.GetComponent<Piece>() : null;
            return IsWoodMaterial(prefab) && piece.m_enabled
                && piece.m_category == Piece.PieceCategory.BuildingWorkbench
                && piece.m_resources.Length > 0
                && piece.m_resources.All(requirement => requirement.m_resItem && requirement.m_resItem.name == "Wood");
        }

        // Pour le journal : pourquoi une pièce wood_ n'est pas copiée.
        static string Describe(GameObject prefab)
        {
            var piece = prefab.GetComponent<Piece>();
            if (!piece) return $"{prefab.name} (pas de Piece)";
            var wear = prefab.GetComponent<WearNTear>();
            var resources = string.Join("+", piece.m_resources.Select(r => $"{(r.m_resItem ? r.m_resItem.name : "?")}x{r.m_amount}"));
            return $"{prefab.name} ({piece.m_category}, {resources}, {(wear ? wear.m_materialType.ToString() : "pas de WearNTear")}{(piece.m_enabled ? "" : ", désactivée")})";
        }

        static bool IsWoodMaterial(GameObject prefab)
        {
            var wear = prefab ? prefab.GetComponent<WearNTear>() : null;
            return wear && prefab.GetComponent<Piece>() && wear.m_materialType == WearNTear.MaterialType.Wood;
        }

        static PieceConfig ConfigFrom(Piece original)
        {
            var config = new PieceConfig
            {
                // Localize traduit chaque jeton : « $piece_woodwall ($emeraude_resine) » → « Mur en bois (résiné) ».
                Name = $"{original.m_name} ($emeraude_resine)",
                Description = "$emeraude_resine_desc",
                PieceTable = PieceTables.Hammer,
                Category = Category,
                CraftingStation = original.m_craftingStation ? original.m_craftingStation.name : "",
            };
            // Le double du bois plutôt que de la résine, trop longue à récolter. Les autres ressources ne changent pas.
            foreach (var requirement in original.m_resources.Where(r => r.m_resItem))
            {
                var factor = requirement.m_resItem.name == "Wood" ? WoodFactor : 1;
                config.AddRequirement(new RequirementConfig(requirement.m_resItem.name, requirement.m_amount * factor, 0, requirement.m_recover));
            }
            return config;
        }

        // Matériaux copiés une fois chacun : ceux de la copie sont encore partagés avec la pièce d'origine.
        static void Tint(GameObject prefab, Dictionary<Material, Material> tinted)
        {
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (!materials[i] || !materials[i].HasProperty("_Color")) continue;
                    if (!tinted.TryGetValue(materials[i], out var material))
                    {
                        material = new Material(materials[i]) { color = materials[i].color * Amber };
                        tinted[materials[i]] = material;
                    }
                    materials[i] = material;
                }
                renderer.sharedMaterials = materials;
            }
        }
    }
}
