using UnityEngine;

namespace LevelGeneration.Terrain
{
    [RequireComponent(typeof(Camera))]
    public class TerrainObserver : MonoBehaviour
    {
        Camera m_Camera;

        void Awake()
        {
            m_Camera = GetComponent<Camera>();
        }

        void OnEnable()
        {
            ProceduralTerrain.ObserverCamera = m_Camera;
        }

        void OnDisable()
        {
            if (ProceduralTerrain.ObserverCamera == m_Camera)
                ProceduralTerrain.ObserverCamera = null;
        }
    }
}
