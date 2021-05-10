using Microsoft.Xna.Framework;
using Monocle;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace Celeste.Mod.DashlessDreamBlocks {
    public class DashlessDreamBlocksModule : EverestModule {
        public static DashlessDreamBlocksModule Instance;

        public override Type SettingsType => typeof(DashlessDreamBlocksSettings);
        public static DashlessDreamBlocksSettings Settings => (DashlessDreamBlocksSettings) Instance._Settings;

        public static int StDashlessDreamDash;

        public DashlessDreamBlocksModule() {
            Instance = this;
        }

        private static List<IDetour> stateHooks = new List<IDetour>();
        private static string[] names = new string[] { "Normal", "Climb", "Swim", "StarFly" };

        public override void Load() {
            // Add StDashlessDreamDash (intermediary state to replace the DashBegin and DashCoroutine)
            On.Celeste.Player.ctor += Player_ctor;

            // Hook Update methods for all states that DreamDashing should be possible from
            Delegate hook_Player_StateUpdate = new Func<Func<Player, int>, Player, int>(Player_StateUpdate);
            foreach (string name in names) {
                MethodInfo stateUpdate = typeof(Player).GetMethod(name + "Update", BindingFlags.NonPublic | BindingFlags.Instance);
                stateHooks.Add(new Hook(stateUpdate, hook_Player_StateUpdate));
            }


            IL.Celeste.Player.DreamDashCheck += Player_DreamDashCheck;

            // Restrict the angles allowed for a DashlessDreamDash
            IL.Celeste.PlayerDashAssist.Update += PlayerDashAssist_Update;
        }

        public override void Unload() {
            On.Celeste.Player.ctor -= Player_ctor;

            stateHooks.ForEach(h => h.Dispose());

            IL.Celeste.Player.DreamDashCheck -= Player_DreamDashCheck;

            IL.Celeste.PlayerDashAssist.Update -= PlayerDashAssist_Update;
        }

        /// <summary>
        /// Adds a new State to the <see cref="Player.StateMachine" /> using JaThePlayer's StateMachine extension code.
        /// </summary>
        /// <remarks>The value for the new state is assigned to <see cref="StDashlessDreamDash" />.</remarks>
        private static void Player_ctor(On.Celeste.Player.orig_ctor orig, Player self, Vector2 position, PlayerSpriteMode spriteMode) {
            orig(self, position, spriteMode);
            StDashlessDreamDash = self.StateMachine.AddState(self.DashlessUpdate, self.DashlessCoroutine, self.DashlessBegin, null);
        }

        private static MethodInfo m_DreamDashCheck = typeof(Player).GetMethod("DreamDashCheck", BindingFlags.NonPublic | BindingFlags.Instance);
        private static FieldInfo f_demoDashed = typeof(Player).GetField("demoDashed", BindingFlags.NonPublic | BindingFlags.Instance);
        private static FieldInfo f_lastAim = typeof(Player).GetField("lastAim", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>
        /// Hooks a Player <see cref="Monocle.StateMachine" /> Update method to set the player state to <see cref="StDashlessDreamDash" />.
        /// </summary>
        private static int Player_StateUpdate(Func<Player, int> orig, Player self) {
            Vector2 lastAim = (Vector2) f_lastAim.GetValue(self);
            if (!(self.Dashes > 0) && (Input.DashPressed || Input.CrouchDashPressed) && 
                (DashlessDreamDashCheck(self, lastAim.YComp()) || DashlessDreamDashCheck(self, lastAim.XComp()) && 
                (TalkComponent.PlayerOver == null || !Input.Talk.Pressed))) {
                f_demoDashed.SetValue(self, Input.CrouchDashPressed);
                Input.Dash.ConsumeBuffer();
                Input.CrouchDash.ConsumeBuffer();
                self.DashDir = lastAim;
                return StDashlessDreamDash;
            }

            return orig(self);
        }

        private static bool dashlesscheck = false;

        /// <summary>
        /// Calls the private `Player.DreamDashCheck` method after setting a static variable referenced in the <see cref="Player_DreamDashCheck" /> IL hook.
        /// </summary>
        private static bool DashlessDreamDashCheck(Player player, Vector2 dir) {
            if (dir.Y < 0)
                dir.Y *= 3;
            Vector2 dashDir = player.DashDir;
            player.DashDir = dir;
            dashlesscheck = true;
            bool res = (bool) m_DreamDashCheck.Invoke(player, new object[]{ dir });
            if (res)
                Extensions.DreamBlockDir = dir;
            dashlesscheck = false;
            player.DashDir = dashDir;
            return res;
        }

        /// <summary>
        /// Replaces <see cref="Player.DashAttacking" /> with
        /// <code>
        ///     (<see cref="Player.DashAttacking" /> || 
        ///     (<see cref="dashlesscheck" /> &amp;&amp; <see cref="DashlessDreamBlocksSettings.Enabled" />))
        /// </code>
        /// </summary>
        private static void Player_DreamDashCheck(ILContext ctx) {
            ILCursor cursor = new ILCursor(ctx);
            if (cursor.TryGotoNext(MoveType.After, instr => instr.MatchCallvirt<Player>("get_DashAttacking"))) {
                cursor.EmitDelegate<Func<bool, bool>>(val => val || (dashlesscheck && Settings.Enabled));
            }
        }

        /// <summary>
        /// Confines the <see cref="PlayerDashAssist" /> angle to what is reasonable for a DashlessDreamDash.
        /// </summary>
        /// <remarks>If the angle is more than 45° from the allowed range it is ignored, otherwise it is clamped.</remarks>
        private static float CorrectDashAngle(float angle) {
            Player player = Engine.Scene.Tracker.GetEntity<Player>();
            if (player != null && player.StateMachine.State == StDashlessDreamDash) {
                float initialAngle = Extensions.DreamBlockDir.Angle();
                float diff = Calc.AngleDiff(initialAngle, angle);
                if (Math.Abs(diff) >= Calc.QuarterCircle) {
                    if (Math.Abs(diff) > Calc.EighthCircle * 3)
                        return initialAngle;
                    return initialAngle + Calc.EighthCircle * Math.Sign(diff);
                }
            }
            return angle;
        }

        /// <summary>
        /// Emits a Delegate to replace the AimVector angle with a corrected one using <see cref="CorrectDashAngle" />.
        /// </summary>
        private static void PlayerDashAssist_Update(ILContext ctx) {
            ILCursor cursor = new ILCursor(ctx);
            if (cursor.TryGotoNext(MoveType.After, instr => instr.MatchCall("Monocle.Calc", "Angle"))) {
                cursor.EmitDelegate<Func<float, float>>(angle => {
                    float corrected = CorrectDashAngle(angle);
                    Extensions.DashAssistOverride = corrected;
                    return corrected;
                });
            }
        }

    }

    public static class Extensions {
        private static FieldInfo f_onGround = typeof(Player).GetField("onGround", BindingFlags.NonPublic | BindingFlags.Instance);
        private static FieldInfo f_demoDashed = typeof(Player).GetField("demoDashed", BindingFlags.NonPublic | BindingFlags.Instance);
        private static FieldInfo f_lastAim = typeof(Player).GetField("lastAim", BindingFlags.NonPublic | BindingFlags.Instance);
        private static MethodInfo m_DashAssistInit = typeof(Player).GetMethod("DashAssistInit", BindingFlags.NonPublic | BindingFlags.Instance);
        private static MethodInfo m_CorrectDashPrecision = typeof(Player).GetMethod("CorrectDashPrecision", BindingFlags.NonPublic | BindingFlags.Instance);
        private static MethodInfo m_CreateTrail = typeof(Player).GetMethod("CreateTrail", BindingFlags.NonPublic | BindingFlags.Instance);

        public static Vector2 DreamBlockDir;
        public static float? DashAssistOverride;

        public static void DashlessBegin(this Player self) {
            if (Engine.TimeRate > 0.25f)
                Celeste.Freeze(0.05f);

            if (!SaveData.Instance.Assists.DashAssist)
                Input.Rumble(RumbleStrength.Strong, RumbleLength.Medium);

            self.Speed = Vector2.Zero;
            self.DashDir = Vector2.Zero;

            if (!(bool) f_onGround.GetValue(self) && self.Ducking && self.CanUnDuck)
                self.Ducking = false;
            else if (!self.Ducking && ((bool) f_demoDashed.GetValue(self) || Input.MoveY.Value == 1))
                self.Ducking = true;

            DashAssistOverride = null;
            m_DashAssistInit.Invoke(self, null);
        }

        private static Vector2 ApplyDashLenience(Vector2 before, Vector2 after) {
            if (Calc.AbsAngleDiff(before.Angle(), after.Angle()) < Calc.QuarterCircle)
                return after;
            return before;
        }

        public static Vector2 ToVector(this float angle, float length = 1) => Calc.AngleToVector(angle, length);

        public static IEnumerator DashlessCoroutine(this Player self) {
            yield return null;

            if (SaveData.Instance.Assists.DashAssist)
                Input.Rumble(RumbleStrength.Strong, RumbleLength.Medium);

            Vector2 dashDir = ApplyDashLenience(DreamBlockDir, DashAssistOverride?.ToVector() ?? (Vector2) f_lastAim.GetValue(self));
            if (self.OverrideDashDirection.HasValue)
                dashDir = self.OverrideDashDirection.Value;
            dashDir = (Vector2) m_CorrectDashPrecision.Invoke(self, new object[]{ dashDir });
            self.Speed = dashDir * 240f;
            self.DashDir = dashDir;

            if (self.DashDir.X != 0f)
                self.Facing = (Facings)Math.Sign(self.DashDir.X);

            if (self.StateMachine.PreviousState == Player.StStarFly)
                self.SceneAs<Level>().Particles.Emit(FlyFeather.P_Boost, 12, self.Center, Vector2.One * 4f, (-dashDir).Angle());

            m_CreateTrail.Invoke(self, null);
            self.Play(SFX.char_mad_jump_dreamblock);

            self.StateMachine.State = Player.StDreamDash;
        }

        public static int DashlessUpdate(this Player self) => DashlessDreamBlocksModule.StDashlessDreamDash;

        #region JaThePlayer's state machine extension code

        /// <summary>
        /// Adds a state to a StateMachine
        /// </summary>
        /// <returns>The index of the new state</returns>
        public static int AddState(this StateMachine machine, Func<int> onUpdate, Func<IEnumerator> coroutine = null, Action begin = null, Action end = null) {
            Action[] begins = (Action[]) StateMachine_begins.GetValue(machine);
            Func<int>[] updates = (Func<int>[]) StateMachine_updates.GetValue(machine);
            Action[] ends = (Action[]) StateMachine_ends.GetValue(machine);
            Func<IEnumerator>[] coroutines = (Func<IEnumerator>[]) StateMachine_coroutines.GetValue(machine);
            int nextIndex = begins.Length;
            // Now let's expand the arrays
            Array.Resize(ref begins, begins.Length + 1);
            Array.Resize(ref updates, begins.Length + 1);
            Array.Resize(ref ends, begins.Length + 1);
            Array.Resize(ref coroutines, coroutines.Length + 1);
            // Store the resized arrays back into the machine
            StateMachine_begins.SetValue(machine, begins);
            StateMachine_updates.SetValue(machine, updates);
            StateMachine_ends.SetValue(machine, ends);
            StateMachine_coroutines.SetValue(machine, coroutines);
            // And now we add the new functions
            machine.SetCallbacks(nextIndex, onUpdate, coroutine, begin, end);
            return nextIndex;
        }
        private static FieldInfo StateMachine_begins = typeof(StateMachine).GetField("begins", BindingFlags.Instance | BindingFlags.NonPublic);
        private static FieldInfo StateMachine_updates = typeof(StateMachine).GetField("updates", BindingFlags.Instance | BindingFlags.NonPublic);
        private static FieldInfo StateMachine_ends = typeof(StateMachine).GetField("ends", BindingFlags.Instance | BindingFlags.NonPublic);
        private static FieldInfo StateMachine_coroutines = typeof(StateMachine).GetField("coroutines", BindingFlags.Instance | BindingFlags.NonPublic);

        #endregion

    }

}
