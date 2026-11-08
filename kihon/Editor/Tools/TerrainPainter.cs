//本代码用于lmCLIforUnity，实现地形绘制的命令
using System;
using UnityEditor;
using UnityEngine;

namespace Lmbox.AIAgent.Tools
{
    internal static class TerrainPainter
    {
        // 通过 TerrainPainter.Fill(terrain, layerIndex) 调用，无需创建 TerrainPainter 实例。
        public static void Fill(Terrain terrain, int layerIndex)
        {
            if (terrain == null || terrain.terrainData == null)
            {
                throw new ArgumentException("没有可用的地形");
            }

            TerrainData data = terrain.terrainData;

            int width = data.alphamapWidth;
            int height = data.alphamapHeight;
            int layers = data.alphamapLayers;

            if (layerIndex < 0 || layerIndex >= layers)
            {
                throw new ArgumentException("地形图层编号不存在");
            }

            float[,,] weights = new float[height, width, layers];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    weights[y, x, layerIndex] = 1f;
                }
            }

            Undo.RegisterCompleteObjectUndo(data, "lmu3d 绘制地形");
            Undo.RegisterCompleteObjectUndo(
                data.alphamapTextures,
                "lmu3d 绘制地形"
            );

            data.SetAlphamaps(0, 0, weights);
            EditorUtility.SetDirty(data);
        }
    }
}
