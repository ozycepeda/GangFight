using UnityEngine;

namespace GangFight
{
    public sealed class PlayHud : MonoBehaviour
    {
        private static readonly Color Panel = new(.06f, .086f, .13f, .95f);
        private static readonly Color Text = new(.96f, .96f, .97f);
        private static readonly Color Muted = new(.68f, .77f, .81f);
        private Arena arena;
        private GUIStyle title, section, label, minor, center, button;

        public void Connect(Arena value) => arena = value;

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.R))
                arena.Restart();
        }

        private void OnGUI()
        {
            if (arena == null)
                return;
            if (title == null)
                PrepareStyles();

            float scale = Mathf.Clamp(Screen.width / 1200f, .64f, 1.15f);
            float width = Screen.width / scale;
            float height = Screen.height / scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            Frame(new Rect(24, 22, 294, 112), Panel);
            Frame(new Rect(24, 22, 4, 112), Visuals.ColorFor(0));
            GUI.Label(new Rect(42, 32, 255, 42), "GANGFIGHT", title);
            GUI.Label(new Rect(43, 81, 252, 25), $"RONDA {arena.Round:D2}  ·  ARENA DE PRUEBAS", minor);
            for (int slot = 0; slot < arena.Count; slot++)
                FighterStatus(slot, new Rect(width - 218, 22 + slot * 52, 194, 45));

            float bar = height - 113;
            Frame(new Rect(24, bar, width - 48, 89), Panel);
            GUI.Label(new Rect(40, bar + 6, width - 80, 23), "W A S D   Mover     Espacio   Saltar     I   Alzar brazos     K   Cabezazo     ,   Patada", label);
            GUI.Label(new Rect(40, bar + 27, width - 80, 23), "J / L   Toca para golpear · Mantén para agarrar · Sujeta con ambas para alzar", label);
            GUI.Label(new Rect(40, bar + 56, 110, 21), "R   Reiniciar", minor);
            RivalButton(new Rect(160, bar + 53, 104, 26), "Muñeco", "passive");
            RivalButton(new Rect(272, bar + 53, 104, 26), "Bot", "edge");
            if (arena.HasPolicy)
                RivalButton(new Rect(384, bar + 53, 104, 26), "Modelo", "model:Champion");

            if (arena.Finished)
            {
                string result = arena.Winner < 0 ? "EMPATE" : arena.Winner == 0 ? "GANASTE" : $"GANA RIVAL {arena.Winner}";
                Frame(new Rect(width / 2 - 153, height * .31f, 306, 75), Panel);
                GUI.Label(new Rect(width / 2 - 153, height * .31f, 306, 75), result, center);
            }
        }

        private void FighterStatus(int slot, Rect row)
        {
            Frame(row, Panel);
            Frame(new Rect(row.x + 12, row.y + 17, 11, 11), Visuals.ColorFor(slot));
            Fighter fighter = arena.Get(slot);
            string state = fighter.Eliminated ? "FUERA" : fighter.KoTime > 0 ? $"KO {fighter.KoTime:F1}s" : HitLabel(fighter);
            GUI.Label(new Rect(row.x + 33, row.y + 3, 66, 25), slot == 0 ? "TÚ" : $"RIVAL {slot}", section);
            GUI.Label(new Rect(row.x + 101, row.y + 4, 87, 25), state, minor);
            if (fighter.Eliminated)
                return;
            Frame(new Rect(row.x + 33, row.y + 32, 142, 4), new Color(.25f, .32f, .38f));
            float stagger = fighter.KoTime > 0 ? 1 : fighter.Stagger;
            Frame(new Rect(row.x + 33, row.y + 32, 142 * stagger, 4),
                fighter.HitFlashTime > 0 ? new Color(1, .66f, .34f) : Visuals.ColorFor(slot));
        }

        private static string HitLabel(Fighter fighter)
        {
            if (fighter.HitFlashTime <= 0)
                return "EN ARENA";
            return fighter.LatestHit switch
            {
                "headbutt" => "CABEZAZO",
                "kick" => "PATADA",
                _ => "PUÑETAZO"
            };
        }

        private void RivalButton(Rect rect, string caption, string role)
        {
            Frame(rect, arena.RivalRole == role ? new Color(.18f, .34f, .39f) : new Color(.13f, .18f, .25f));
            if (!GUI.Button(rect, caption, button))
                return;
            arena.SetRival(role);
        }

        private static void Frame(Rect rect, Color color)
        {
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        private void PrepareStyles()
        {
            title = Style(35, Text, FontStyle.Bold);
            section = Style(16, Text, FontStyle.Bold);
            label = Style(16, Text);
            minor = Style(12, Muted, FontStyle.Bold);
            center = Style(29, Text, FontStyle.Bold);
            center.alignment = TextAnchor.MiddleCenter;
            button = Style(14, Text, FontStyle.Bold);
            button.alignment = TextAnchor.MiddleCenter;
            button.hover.textColor = Visuals.ColorFor(1);
        }

        private static GUIStyle Style(int size, Color color, FontStyle weight = FontStyle.Normal)
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = weight };
            style.normal.textColor = color;
            return style;
        }
    }
}
