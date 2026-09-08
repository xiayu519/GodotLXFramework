using EvalBase.Gameplay;

static class Probe
{
    static int assertions;
    static void Check(bool value, string name) { assertions++; if (!value) throw new Exception(name); }
    static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { assertions++; return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    static void Main()
    {
        Throws<ArgumentOutOfRangeException>(() => new RoundState(0));
        Throws<ArgumentOutOfRangeException>(() => new RoundState(-1));
        var state = new RoundState(3);
        Check(state.Score == 0 && state.IsRunning && !state.HasWon && state.RoundNumber == 1, "initial");
        state.AddScore(1); state.Lose(); state.AddScore(9);
        Check(state.Score == 1 && !state.IsRunning && !state.HasWon, "lose freezes score");
        Throws<ArgumentOutOfRangeException>(() => state.AddScore(-1));
        for (var round = 2; round <= 5; round++)
        {
            state.Restart(); state.AddScore(int.MaxValue);
            Check(state.RoundNumber == round && state.Score == 3 && state.HasWon && !state.IsRunning, "win clamp/restart");
        }
        var huge = new RoundState(int.MaxValue);
        huge.AddScore(int.MaxValue - 1); huge.AddScore(int.MaxValue);
        Check(huge.Score == int.MaxValue && huge.HasWon, "overflow-safe clamp");
        var random = new Random(8128);
        state = new RoundState(7);
        int score = 0, number = 1; bool running = true, won = false;
        for (var i = 0; i < 500; i++)
        {
            var op = random.Next(3);
            if (op == 0)
            {
                int amount = random.Next(20);
                state.AddScore(amount);
                if (running) { score = Math.Min(7, score + amount); if (score == 7) { running = false; won = true; } }
            }
            else if (op == 1) { state.Lose(); if (running) running = false; }
            else { state.Restart(); score = 0; running = true; won = false; number++; }
            Check(state.Score == score && state.RoundNumber == number && state.IsRunning == running && state.HasWon == won, "sequence " + i);
        }
        Console.WriteLine("LX_OUTCOME_PASS round-state assertions=" + assertions);
    }
}
