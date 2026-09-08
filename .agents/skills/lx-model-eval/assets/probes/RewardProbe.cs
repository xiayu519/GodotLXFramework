using EvalBase.Gameplay;

static class Probe
{
    static int assertions;
    static readonly CancellationToken None = CancellationToken.None;
    static Task Ready(CancellationToken _) => Task.CompletedTask;
    static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    static void Check(bool value, string name) { assertions++; if (!value) throw new Exception(name); }
    static async Task<bool> Done(Task<bool> task) => await task.WaitAsync(TimeSpan.FromSeconds(5));
    static async Task Throws<T>(Func<Task> action) where T : Exception
    {
        try { await action().WaitAsync(TimeSpan.FromSeconds(5)); } catch (T) { assertions++; return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    static async Task Main()
    {
        var ledger = new RewardLedger();
        Check(ledger.Total == 0, "initial");
        await Throws<ArgumentException>(() => ledger.AwardAsync("", 1, Ready, None));
        await Throws<ArgumentOutOfRangeException>(() => ledger.AwardAsync("a", 0, Ready, None));
        await Throws<ArgumentNullException>(() => ledger.AwardAsync("a", 1, null!, None));
        Check(await Done(ledger.AwardAsync("a", 3, Ready, None)), "first");
        Check(!await Done(ledger.AwardAsync("a", 3, _ => throw new Exception("duplicate prepare"), None)) && ledger.Total == 3, "once");
        var gate = Gate();
        var pending = ledger.AwardAsync("pending", 2, _ => gate.Task, None);
        Check(!pending.IsCompleted && ledger.Total == 3, "no early commit");
        Check(!await Done(ledger.AwardAsync("pending", 2, _ => throw new Exception("duplicate"), None)), "pending duplicate returns");
        gate.SetResult(); Check(await Done(pending) && ledger.Total == 5, "commit");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Check(!await Done(ledger.AwardAsync("cancel", 7, Ready, canceled.Token)) && ledger.Total == 5, "pre-canceled");
        Check(await Done(ledger.AwardAsync("cancel", 7, Ready, None)), "canceled id reusable");
        using var laterCancel = new CancellationTokenSource();
        gate = Gate(); pending = ledger.AwardAsync("later", 9, _ => gate.Task, laterCancel.Token);
        laterCancel.Cancel(); gate.SetResult();
        Check(!await Done(pending) && ledger.Total == 12, "canceled while pending");
        await Throws<InvalidOperationException>(() => ledger.AwardAsync("fault", 1, _ => throw new InvalidOperationException(), None));
        Check(await Done(ledger.AwardAsync("fault", 1, Ready, None)), "fault releases reservation");
        // Deliberately complete a stale operation while the same ID is pending in a new generation.
        var oldGate = Gate(); var old = ledger.AwardAsync("same", 100, _ => oldGate.Task, None);
        ledger.Restart(); var newGate = Gate(); var current = ledger.AwardAsync("same", 4, _ => newGate.Task, None);
        oldGate.SetResult(); Check(!await Done(old) && ledger.Total == 0, "stale generation rejected");
        Check(!await Done(ledger.AwardAsync("same", 8, _ => throw new Exception("old finally removed new reservation"), None)), "generation-owned cleanup");
        newGate.SetResult(); Check(await Done(current) && ledger.Total == 4, "new generation commits");
        ledger.Restart();
        Check(await Done(ledger.AwardAsync("max", int.MaxValue, Ready, None)), "maximum total");
        await Throws<OverflowException>(() => ledger.AwardAsync("overflow", 1, Ready, None));
        Check(ledger.Total == int.MaxValue, "overflow rollback");
        ledger.Restart();
        var concurrentGate = Gate(); int prepared = 0;
        var concurrent = Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
            ledger.AwardAsync("concurrent", 11, _ => { Interlocked.Increment(ref prepared); return concurrentGate.Task; }, None))).ToArray();
        // Wait for the winner to enter prepare without depending on timing for correctness.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (Volatile.Read(ref prepared) == 0 && DateTime.UtcNow < deadline) await Task.Yield();
        concurrentGate.SetResult();
        var answers = await Task.WhenAll(concurrent).WaitAsync(TimeSpan.FromSeconds(5));
        Check(answers.Count(x => x) == 1 && prepared == 1 && ledger.Total == 11, "concurrent exactly once");
        // prepare may synchronously call Restart from another thread: it must not run under the ledger lock.
        Check(!await Done(ledger.AwardAsync("reentrant", 1, _ =>
        {
            Check(Task.Run(ledger.Restart).Wait(TimeSpan.FromSeconds(3)), "prepare outside lock");
            return Task.CompletedTask;
        }, None)) && ledger.Total == 0, "reentrant restart");
        Console.WriteLine("LX_OUTCOME_PASS reward-ledger assertions=" + assertions);
    }
}
