using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using MenuLib;
using MenuLib.MonoBehaviors;
using UnityEngine;
using UnityEngine.UI;

namespace StatsCore
{
    // Puts a Stats button in the main menu list and the pause menu. Only touched when MenuLib is
    // loaded, so StatsCore still works as a plain library without it.
    internal static class StatsScreenEntry
    {
        internal static void Install(ConfigFile config)
        {
            ConfigEntry<bool> main = config.Bind("Stats screen", "Main menu button", true, "Show a Stats button in the main menu.");
            ConfigEntry<bool> pause = config.Bind("Stats screen", "Pause menu button", true, "Show a Stats button in the pause menu.");

            if (main.Value)
                new Harmony("Vippy.StatsCore.Screen").PatchAll(typeof(MainMenuPatch));
            if (pause.Value)
                MenuAPI.AddElementToEscapeMenu(parent => MenuAPI.CreateREPOButton("Stats", StatsPage.Open, parent, new Vector2(126f, 29f)));
        }
    }

    [HarmonyPatch(typeof(MenuPageMain), "Start")]
    internal static class MainMenuPatch
    {
        // Same approach REPOConfig takes for its Mods button, so both sit in the list as vanilla buttons do:
        // slot ours in just above Quit and space the whole column evenly again.
        [HarmonyPostfix]
        private static void Postfix(MenuPageMain __instance)
        {
            Transform? holder = __instance.transform.Find("LeftTransform/Buttons");
            if (holder == null) return;

            if (!holder.TryGetComponent(out VerticalLayoutGroup _))
            {
                var group = holder.gameObject.AddComponent<VerticalLayoutGroup>();
                group.childControlWidth = false;
                group.childForceExpandWidth = false;
            }

            REPOButton button = MenuAPI.CreateREPOButton("Stats", StatsPage.Open, holder, Vector2.zero);
            List<Transform> list = new List<Transform>();
            foreach (Transform child in holder)
            {
                if (child != button.transform && (child.name.Contains("Menu Button") || child.GetComponent<REPOButton>() != null))
                    list.Add(child);
            }
            list = list.OrderByDescending(t => t.localPosition.y).ToList();
            if (list.Count == 0) return;

            list.Insert(list.Count - 1, button.transform);
            button.transform.SetSiblingIndex(list[list.Count - 1].GetSiblingIndex());

            float y = 230f;
            foreach (Transform item in list)
            {
                Vector3 position = item.localPosition;
                position.y = y;
                item.localPosition = position;
                y -= 30f;
            }
        }
    }
}
