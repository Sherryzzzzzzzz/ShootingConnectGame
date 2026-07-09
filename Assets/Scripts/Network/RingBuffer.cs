/// <summary>
/// Simple ring buffer for storing items indexed by tick number.
/// </summary>
public class RingBuffer<T>
{
    private readonly T[] _buffer;
    private readonly int _capacity;

    public RingBuffer(int capacity)
    {
        _capacity = capacity;
        _buffer = new T[capacity];
    }

    public void Store(int tick, T item)
    {
        _buffer[tick % _capacity] = item;
    }

    public T Get(int tick)
    {
        return _buffer[tick % _capacity];
    }

    public int Capacity => _capacity;
}
