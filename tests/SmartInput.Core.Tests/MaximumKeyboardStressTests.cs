using System.Security.Cryptography;
using System.Text;
using SmartInput.Core.Configuration;
using SmartInput.Core.Models;
using SmartInput.Core.Services;
using SmartInput.Platform.Abstractions.Input;
using SmartInput.Platform.Abstractions.Security;
using SmartInput.Platform.Windows.Input;
using PlatformKeyEventType = SmartInput.Platform.Abstractions.Input.KeyEventType;

namespace SmartInput.Core.Tests;

[Trait("Category", "KeyboardStress")]
public class MaximumKeyboardStressTests
{
    private const int MinCharacterKeyDowns = 100_000;
    private const int MinKeyUps = 100_000;
    private const int MinBoundaries = 25_000;
    private const int MinFocusTransitions = 10_000;
    private const int MinPolicyTransitions = 10_000;
    private const int MinDoubleShiftSequences = 10_000;
    private const int MaxPendingBound = 64;

    private static readonly (int VirtualKey, int ScanCode)[] BoundaryKeys =
    [
        (VirtualKeys.Space, 57),
        (VirtualKeys.Tab, 15),
        (VirtualKeys.Return, 28),
        (VirtualKeys.OemComma, 51),
        (VirtualKeys.OemPeriod, 52),
    ];

    private static readonly char[] TokenAlphabet =
        "abcdefghijklmnopqrstuvwxyzабвгдеёжзийклмнопрстуфхцчшщъыьэюя".ToCharArray();

    [Theory]
    [InlineData(42)]
    [InlineData(1337)]
    [InlineData(20260903)]
    public void StressSimulation_MaintainsPairingDeliveryAndDeterminismInvariants(int seed)
    {
        var first = KeyboardStressHarness.Run(seed);
        var second = KeyboardStressHarness.Run(seed);

        Assert.True(first.CharacterKeyDowns >= MinCharacterKeyDowns, $"KeyDown chars={first.CharacterKeyDowns}");
        Assert.True(first.KeyUps >= MinKeyUps, $"KeyUps={first.KeyUps}");
        Assert.True(first.Boundaries >= MinBoundaries, $"Boundaries={first.Boundaries}");
        Assert.True(first.FocusTransitions >= MinFocusTransitions, $"Focus={first.FocusTransitions}");
        Assert.True(first.PolicyTransitions >= MinPolicyTransitions, $"Policy={first.PolicyTransitions}");
        Assert.True(first.DoubleShiftSequences >= MinDoubleShiftSequences, $"DoubleShift={first.DoubleShiftSequences}");

        Assert.Equal(BoundaryPairingState.Idle, first.FinalPairingState);
        Assert.True(first.BufferEmptyAtEnd);
        Assert.Equal(0, first.PendingAtEnd);
        Assert.True(first.MaxPendingObserved <= MaxPendingBound, $"MaxPending={first.MaxPendingObserved}");

        Assert.Equal(
            first.BoundaryRegistrations,
            first.BoundaryDeliveries + first.BoundariesClearedWithoutDelivery);
        Assert.Equal(first.MatchingKeyUpSuppressions, first.TrackerKeyUpSuppressedCount);
        Assert.True(first.MatchingKeyUpSuppressions <= first.BoundaryRegistrations);
        Assert.True(first.AdversarialUnmatchedKeyUps > 0);
        Assert.True(first.DoubleShiftCompletions == first.DoubleShiftSequences);

        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(first.FormatSummary(), second.FormatSummary());
    }

    internal sealed class KeyboardStressSummary
    {
        public int Seed { get; init; }
        public string Fingerprint { get; set; } = string.Empty;
        public int CharacterKeyDowns { get; set; }
        public int KeyUps { get; set; }
        public int MatchingKeyUpSuppressions { get; set; }
        public int AdversarialUnmatchedKeyUps { get; set; }
        public int Boundaries { get; set; }
        public int BoundaryRegistrations { get; set; }
        public int BoundaryDeliveries { get; set; }
        public int BoundariesClearedWithoutDelivery { get; set; }
        public int FocusTransitions { get; set; }
        public int PolicyTransitions { get; set; }
        public int DoubleShiftSequences { get; set; }
        public int DoubleShiftCompletions { get; set; }
        public int MaxPendingObserved { get; set; }
        public int PendingAtEnd { get; set; }
        public int TrackerKeyUpSuppressedCount { get; set; }
        public BoundaryPairingState FinalPairingState { get; set; }
        public bool BufferEmptyAtEnd { get; set; }
        public int PolicyAllowedCount { get; set; }
        public int PolicyBlockedCount { get; set; }

        public string FormatSummary()
        {
            return
                $"seed={Seed}; fp={Fingerprint}; charDown={CharacterKeyDowns}; keyUp={KeyUps}; "
                + $"matchSup={MatchingKeyUpSuppressions}; advUp={AdversarialUnmatchedKeyUps}; "
                + $"bound={Boundaries}; reg={BoundaryRegistrations}; del={BoundaryDeliveries}; "
                + $"cleared={BoundariesClearedWithoutDelivery}; focus={FocusTransitions}; "
                + $"policy={PolicyTransitions}; dshift={DoubleShiftSequences}; "
                + $"dshiftOk={DoubleShiftCompletions}; maxPend={MaxPendingObserved}; "
                + $"allowed={PolicyAllowedCount}; blocked={PolicyBlockedCount}";
        }

        public string FormatCountersForFingerprint()
        {
            return
                $"seed={Seed}; charDown={CharacterKeyDowns}; keyUp={KeyUps}; "
                + $"matchSup={MatchingKeyUpSuppressions}; advUp={AdversarialUnmatchedKeyUps}; "
                + $"bound={Boundaries}; reg={BoundaryRegistrations}; del={BoundaryDeliveries}; "
                + $"cleared={BoundariesClearedWithoutDelivery}; focus={FocusTransitions}; "
                + $"policy={PolicyTransitions}; dshift={DoubleShiftSequences}; "
                + $"dshiftOk={DoubleShiftCompletions}; maxPend={MaxPendingObserved}; "
                + $"allowed={PolicyAllowedCount}; blocked={PolicyBlockedCount}";
        }
    }

    internal static class KeyboardStressHarness
    {
        internal static KeyboardStressSummary Run(int seed)
        {
            var random = new Random(seed);
            var summary = new KeyboardStressSummary { Seed = seed };
            var buffer = new CurrentTokenBuffer();
            // Large wait so stress never depends on wall-clock expiry.
            var tracker = new BoundaryKeyPairingTracker(keyUpWaitMilliseconds: 60_000);
            var pendingDeliveries = new Queue<DeferredBoundaryKey>(MaxPendingBound);
            var evaluator = new SafetyPolicyEvaluator();
            var policyAllowsAutomation = true;
            var awaitingPair = false;
            int awaitingVk = 0;
            int awaitingScan = 0;

            void NotePending()
            {
                summary.MaxPendingObserved = Math.Max(summary.MaxPendingObserved, pendingDeliveries.Count);
                Assert.True(pendingDeliveries.Count <= MaxPendingBound);
            }

            void EnqueueDelivery(DeferredBoundaryKey boundary)
            {
                if (pendingDeliveries.Count >= MaxPendingBound)
                {
                    pendingDeliveries.Dequeue();
                    summary.BoundaryDeliveries++;
                }

                pendingDeliveries.Enqueue(boundary);
                NotePending();
            }

            void DeliverOne()
            {
                if (pendingDeliveries.Count == 0)
                {
                    return;
                }

                pendingDeliveries.Dequeue();
                summary.BoundaryDeliveries++;
                NotePending();
            }

            void ClearAllPending(bool countAsCleared)
            {
                if (countAsCleared)
                {
                    summary.BoundariesClearedWithoutDelivery += pendingDeliveries.Count;
                }

                pendingDeliveries.Clear();
                awaitingPair = false;
                awaitingVk = 0;
                awaitingScan = 0;
                tracker.ClearPending(BoundaryPairingCleanupReason.None);
                NotePending();
            }

            void RegisterBoundary(int virtualKey, int scanCode)
            {
                // Tracker holds a single pending pair — complete or drop before overwrite.
                if (awaitingPair)
                {
                    if (tracker.TrySuppressMatchingKeyUp(awaitingVk, awaitingScan))
                    {
                        summary.KeyUps++;
                        summary.MatchingKeyUpSuppressions++;
                        DeliverOne();
                    }
                    else
                    {
                        tracker.ClearPending(BoundaryPairingCleanupReason.None);
                        if (pendingDeliveries.Count > 0)
                        {
                            pendingDeliveries.Dequeue();
                            summary.BoundariesClearedWithoutDelivery++;
                            NotePending();
                        }
                    }

                    awaitingPair = false;
                }

                var boundary = DeferredBoundaryKey.FromVirtualKey(virtualKey, scanCode);
                tracker.RegisterSuppressedKeyDown(virtualKey, scanCode);
                EnqueueDelivery(boundary);
                awaitingPair = true;
                awaitingVk = virtualKey;
                awaitingScan = scanCode;
                summary.BoundaryRegistrations++;
                summary.Boundaries++;

                buffer.Apply(TokenInputEvent.Boundary(boundary));
            }

            void CompleteAwaitingKeyUp()
            {
                if (!awaitingPair)
                {
                    return;
                }

                summary.KeyUps++;
                if (tracker.TrySuppressMatchingKeyUp(awaitingVk, awaitingScan))
                {
                    summary.MatchingKeyUpSuppressions++;
                    DeliverOne();
                }
                else
                {
                    if (pendingDeliveries.Count > 0)
                    {
                        pendingDeliveries.Dequeue();
                        summary.BoundariesClearedWithoutDelivery++;
                        NotePending();
                    }
                }

                awaitingPair = false;
                awaitingVk = 0;
                awaitingScan = 0;
            }

            void AdversarialKeyUp()
            {
                summary.KeyUps++;
                summary.AdversarialUnmatchedKeyUps++;
                var suppressed = tracker.TrySuppressMatchingKeyUp(VirtualKeys.Left, 75);
                if (!suppressed && awaitingPair)
                {
                    // Wrong scan for the awaiting key must not suppress.
                    suppressed = tracker.TrySuppressMatchingKeyUp(awaitingVk, awaitingScan ^ 0x55);
                }
                else if (!awaitingPair)
                {
                    var key = BoundaryKeys[random.Next(BoundaryKeys.Length)];
                    suppressed = tracker.TrySuppressMatchingKeyUp(key.VirtualKey, key.ScanCode);
                }

                Assert.False(suppressed);
            }

            void FocusTransition()
            {
                summary.FocusTransitions++;
                tracker.ClearPending(BoundaryPairingCleanupReason.FocusChange);
                ClearAllPending(countAsCleared: true);
                buffer.Apply(TokenInputEvent.Reset);
            }

            void PolicyTransition()
            {
                summary.PolicyTransitions++;
                var variant = random.Next(6);
                var context = variant switch
                {
                    0 => SafetyPolicyDefaults.CreateDefaultContext(processName: "notepad"),
                    1 => SafetyPolicyDefaults.CreateDefaultContext(isEmergencyPaused: true, processName: "notepad"),
                    2 => SafetyPolicyDefaults.CreateDefaultContext(
                        secureInputState: SecureInputState.Active,
                        processName: "notepad"),
                    3 => SafetyPolicyDefaults.CreateDefaultContext(
                        secureInputState: SecureInputState.Unknown,
                        processName: "notepad"),
                    4 => SafetyPolicyDefaults.CreateDefaultContext(processName: "powershell"),
                    _ => SafetyPolicyDefaults.CreateDefaultContext(
                        processName: "bankapp",
                        excludedApplications: ["bankapp"]),
                };

                var result = evaluator.Evaluate(context);
                policyAllowsAutomation = result.AllowsAutomation;
                if (policyAllowsAutomation)
                {
                    summary.PolicyAllowedCount++;
                }
                else
                {
                    summary.PolicyBlockedCount++;
                }

                tracker.ClearPending(BoundaryPairingCleanupReason.PolicyChange);
                ClearAllPending(countAsCleared: true);
                buffer.Apply(TokenInputEvent.Reset);
            }

            void DoubleShiftSequence()
            {
                summary.DoubleShiftSequences++;
                var simulator = new DoubleShiftSequenceSimulator();
                var shift = random.Next(2) == 0 ? VirtualKeys.LShift : VirtualKeys.RShift;

                Assert.False(simulator.Observe(shift, PlatformKeyEventType.KeyDown));
                simulator.Observe(shift, PlatformKeyEventType.KeyUp);
                var completed = simulator.Observe(shift, PlatformKeyEventType.KeyDown);
                simulator.Observe(shift, PlatformKeyEventType.KeyUp);

                Assert.True(completed);
                summary.DoubleShiftCompletions++;
            }

            for (var i = 0; i < MinCharacterKeyDowns; i++)
            {
                var ch = TokenAlphabet[random.Next(TokenAlphabet.Length)];
                buffer.Apply(TokenInputEvent.CharacterInput(ch));
                summary.CharacterKeyDowns++;

                // Character KeyUp — never pairs with boundary tracker.
                summary.KeyUps++;
                Assert.False(tracker.TrySuppressMatchingKeyUp(0x41 + (i % 26), i & 0x7F));

                if ((i & 31) == 0)
                {
                    AdversarialKeyUp();
                }
            }

            for (var i = 0; i < MinBoundaries; i++)
            {
                var len = 1 + random.Next(4);
                for (var c = 0; c < len; c++)
                {
                    buffer.Apply(TokenInputEvent.CharacterInput(TokenAlphabet[random.Next(TokenAlphabet.Length)]));
                }

                var key = BoundaryKeys[random.Next(BoundaryKeys.Length)];
                if (policyAllowsAutomation)
                {
                    RegisterBoundary(key.VirtualKey, key.ScanCode);
                    CompleteAwaitingKeyUp();
                }
                else
                {
                    summary.Boundaries++;
                    summary.KeyUps++;
                    Assert.False(tracker.TrySuppressMatchingKeyUp(key.VirtualKey, key.ScanCode));
                }

                if ((i & 7) == 0)
                {
                    AdversarialKeyUp();
                }
            }

            for (var i = 0; i < MinFocusTransitions; i++)
            {
                if ((i & 3) == 0 && policyAllowsAutomation)
                {
                    var key = BoundaryKeys[random.Next(BoundaryKeys.Length)];
                    RegisterBoundary(key.VirtualKey, key.ScanCode);
                }

                FocusTransition();
            }

            for (var i = 0; i < MinPolicyTransitions; i++)
            {
                if ((i & 3) == 0 && policyAllowsAutomation)
                {
                    var key = BoundaryKeys[random.Next(BoundaryKeys.Length)];
                    RegisterBoundary(key.VirtualKey, key.ScanCode);
                }

                PolicyTransition();
            }

            for (var i = 0; i < MinDoubleShiftSequences; i++)
            {
                DoubleShiftSequence();
            }

            CompleteAwaitingKeyUp();
            while (pendingDeliveries.Count > 0)
            {
                DeliverOne();
            }

            tracker.ClearPending(BoundaryPairingCleanupReason.HookShutdown);
            buffer.Apply(TokenInputEvent.Reset);

            summary.PendingAtEnd = pendingDeliveries.Count;
            summary.TrackerKeyUpSuppressedCount = tracker.KeyUpSuppressedCount;
            summary.FinalPairingState = tracker.State;
            summary.BufferEmptyAtEnd = buffer.IsEmpty;

            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(summary.FormatCountersForFingerprint()));
            summary.Fingerprint = Convert.ToHexString(hash)[..16];
            return summary;
        }
    }

    /// <summary>
    /// Lightweight Double-Shift recognizer mirroring DoubleShiftUndoCoordinator rules
    /// without UI / input-monitor dependencies (Core.Tests does not reference App).
    /// </summary>
    private sealed class DoubleShiftSequenceSimulator
    {
        private bool _awaitingSecondTap;
        private bool _shiftIsDown;

        public bool Observe(int virtualKeyCode, PlatformKeyEventType eventType)
        {
            if (!IsShift(virtualKeyCode))
            {
                Reset();
                return false;
            }

            if (eventType == PlatformKeyEventType.KeyDown)
            {
                if (_shiftIsDown)
                {
                    return false;
                }

                _shiftIsDown = true;
                if (_awaitingSecondTap)
                {
                    _awaitingSecondTap = false;
                    return true;
                }

                return false;
            }

            if (eventType == PlatformKeyEventType.KeyUp)
            {
                _shiftIsDown = false;
                _awaitingSecondTap = true;
            }

            return false;
        }

        public void Reset()
        {
            _awaitingSecondTap = false;
            _shiftIsDown = false;
        }

        private static bool IsShift(int virtualKeyCode) =>
            virtualKeyCode is VirtualKeys.Shift or VirtualKeys.LShift or VirtualKeys.RShift;
    }
}
