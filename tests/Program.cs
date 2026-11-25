using GangFight;

static void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
}

var rules = new MatchRules();
rules.Start(4);
rules.Resolve(0b0011, false);
Check(rules.Remaining == 0b1100 && !rules.Finished, "Two eliminations leave a live duel.");
Check(rules.Reward(0) == -1 && rules.Reward(1) == -1 && rules.Reward(2) == 0, "Only eliminated slots lose.");
rules.Resolve(0b0100, true);
Check(rules.Winner == 3 && !rules.Interrupted && rules.Reward(3) == 1, "Natural result wins over timeout.");
Check(rules.Reward(0) == 0 && rules.Reward(2) == -1, "Earlier losses are not delivered again.");
rules.Resolve(8, false);
Check(rules.Winner == 3, "Closed round is immutable.");

rules.Start(2);
rules.Resolve(3, false);
Check(rules.Finished && rules.Winner == -1 && rules.Reward(0) == 0 && rules.Reward(1) == 0, "Simultaneous final falls draw.");
rules.Start(2);
rules.Resolve(0, true);
Check(rules.Finished && rules.Interrupted && rules.Reward(0) == 0, "Timeout truncates without loss.");

var input = new FighterInput();
var full = new FighterCommand(-1, 1, (FighterButtons)63);
Check(input.Sample(full) == (FighterButtons)63 && input.Sample(full) == 0, "Six independent simultaneous buttons and edge detection.");
var leftOnly = new FighterCommand(0, 0, FighterButtons.LeftGrip);
Check(input.Sample(leftOnly) == 0 && input.Previous == FighterButtons.LeftGrip, "Letting go of one hand preserves the other.");
Check(input.Sample(full).HasFlag(FighterButtons.Kick) && input.Previous.HasFlag(FighterButtons.RightGrip), "Buttons rearm after release.");
input.Reset();
Check(input.Previous == 0 && input.Sample(full).HasFlag(FighterButtons.Jump), "Round reset clears previous buttons.");
foreach (int value in new[] { -1, 0, 1 })
    Check(FighterCommand.DecodeAxis(FighterCommand.EncodeAxis(value)) == value, "Axis codec.");
Check((int)FighterButtons.RaiseArms == 1 << 3 && (int)FighterButtons.Headbutt == 1 << 4 &&
    (int)FighterButtons.Kick == 1 << 5, "The I, K and comma action branches match the controls.");

var hand = new HandGesture();
Check(!hand.Sample(true, true, 10) && !hand.Reaching, "A hand press is pending.");
Check(hand.Sample(false, false, 14) && !hand.Reaching, "Short release punches once.");
Check(!hand.Sample(false, false, 16), "Idle release does not repeat the punch.");
hand.Sample(true, true, 20);
Check(!hand.Sample(false, true, 27) && !hand.Reaching, "Reach waits for the hold threshold.");
Check(!hand.Sample(false, true, 28) && hand.Reaching, "Reach begins at the hold threshold.");
Check(!hand.Sample(false, false, 30) && !hand.Reaching, "Releasing a grip never punches.");
hand.Sample(true, true, 40);
Check(!hand.Sample(false, false, 48), "Release at the exact threshold is never a punch.");
Check(!hand.Sample(false, true, 60) && !hand.Reaching, "A canceled hold needs a new press.");

var ffa = RunOptions.Parse(new[] { "--gf-mode", "ffa", "--gf-opponents", "edge,model:Champion,passive", "--gf-seed", "-42" }, false);
Check(ffa.Fighters == 4 && ffa.Roles[0] == "learn" && ffa.Roles[2] == "model:Champion" && ffa.Seed == -42, "FFA config.");
var play = RunOptions.Parse(Array.Empty<string>(), true);
Check(play.Session && play.Fighters == 2 && play.Roles[0] == "keyboard", "Playable scene default.");
Check(Math.Abs(4 * play.HalfSize * play.HalfSize - 150f) < .001f && play.ThrowMultiplier == 1.2f,
    "Arena grows by area and rotational throws default to 1.2.");
var naturalThrow = RunOptions.Parse(new[] { "--gf-throw-multiplier", "1", "--gf-half-size", "5" }, true);
Check(naturalThrow.ThrowMultiplier == 1 && naturalThrow.HalfSize == 5, "Throw boost and arena size can be overridden.");
try
{
    RunOptions.Parse(new[] { "--gf-mode", "ffa", "--gf-opponents", "keyboard,learn" }, false);
    throw new InvalidOperationException("Invalid opponent accepted.");
}
catch (ArgumentException) { }
Console.WriteLine("GANGFIGHT_RULES_OK");
