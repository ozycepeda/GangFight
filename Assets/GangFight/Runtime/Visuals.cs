using UnityEngine;

namespace GangFight
{
    internal static class Visuals
    {
        static readonly Color[] Suits = { new(.97f, .37f, .22f), new(.07f, .72f, .75f), new(.96f, .72f, .23f), new(.66f, .52f, .99f) };
        public static readonly bool Enabled = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
        private static Shader shader;
        private static Material ink;
        public static Material Ink => ink ? ink : ink = Paint(new Color(.042f, .055f, .092f));
        public static Color ColorFor(int slot) => Suits[slot];

        public static Material Paint(Color color)
        {
            if (!Enabled)
                return null;
            if (!shader)
                shader = Resources.Load<Shader>("GangFightSurface");
            return new Material(shader) { color = color };
        }

        public static GameObject Part(string name, PrimitiveType shape, Transform parent,
            Vector3 localPosition, Vector3 localScale, Material paint)
        {
            GameObject part = Enabled ? GameObject.CreatePrimitive(shape) : new GameObject();
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            if (Enabled)
            {
                Collider collider = part.GetComponent<Collider>();
                collider.enabled = false;
                Object.Destroy(collider);
                part.GetComponent<MeshRenderer>().sharedMaterial = paint;
            }
            return part;
        }
    }
}
