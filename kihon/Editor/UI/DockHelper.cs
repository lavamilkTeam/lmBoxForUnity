using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Lmbox.AIAgent.UI
{
    /// <summary>
    /// Unity has no public API for docking a window at a specific location,
    /// so this uses reflection (EditorWindow.m_Parent → DockArea.AddTab) to dock
    /// the agent window as a tab next to the Inspector — i.e. the right side of
    /// the default layout. Best-effort: falls back to a plain docked window.
    /// </summary>
    public static class DockHelper
    {
        public static void DockNextToInspector(EditorWindow window)
        {
            try
            {
                var inspectorType = typeof(EditorWindow).Assembly.GetType("UnityEditor.InspectorWindow");
                if (inspectorType == null) return;

                var inspectors = Resources.FindObjectsOfTypeAll(inspectorType);
                if (inspectors == null || inspectors.Length == 0) return;

                var parentField = typeof(EditorWindow).GetField(
                    "m_Parent", BindingFlags.Instance | BindingFlags.NonPublic);
                if (parentField == null) return;

                var inspectorDockArea = parentField.GetValue(inspectors[0]);
                if (inspectorDockArea == null) return;

                var addTab = inspectorDockArea.GetType().GetMethod(
                    "AddTab",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    new[] { typeof(EditorWindow), typeof(bool) },
                    null);

                addTab?.Invoke(inspectorDockArea, new object[] { window, true });
            }
            catch (Exception)
            {
                // Docking is best-effort; the window still works wherever it opened.
            }
        }
    }
}
