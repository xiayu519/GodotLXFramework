namespace LX.Core.Random;

public readonly record struct RngState(ulong State, ulong Increment);

public sealed class DeterministicRng
{
    private ulong _state;
    private ulong _increment;

    public DeterministicRng(ulong seed, ulong stream = 54)
    {
        _increment = (stream << 1) | 1;
        _ = NextUInt();
        _state += seed;
        _ = NextUInt();
    }

    public RngState Capture() => new(_state, _increment);

    public void Restore(RngState state)
    {
        if ((state.Increment & 1) == 0)
        {
            throw new ArgumentException("A PCG increment must be odd.", nameof(state));
        }

        _state = state.State;
        _increment = state.Increment;
    }

    public uint NextUInt()
    {
        var oldState = _state;
        _state = unchecked(oldState * 6364136223846793005UL + _increment);
        var xorShifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
        var rotation = (int)(oldState >> 59);
        return (xorShifted >> rotation) | (xorShifted << ((-rotation) & 31));
    }

    public int NextInt(int exclusiveMax)
    {
        if (exclusiveMax <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
        }

        var bound = (uint)exclusiveMax;
        var threshold = unchecked((uint)(0 - bound)) % bound;
        while (true)
        {
            var value = NextUInt();
            if (value >= threshold)
            {
                return (int)(value % bound);
            }
        }
    }

    public int NextInt(int inclusiveMin, int exclusiveMax)
    {
        if (exclusiveMax <= inclusiveMin)
        {
            throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
        }

        return inclusiveMin + NextInt(exclusiveMax - inclusiveMin);
    }

    public double NextDouble() => NextUInt() / ((double)uint.MaxValue + 1.0);
}
