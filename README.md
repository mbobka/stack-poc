# Initial Stack PoC — win64 / musl (5GB Stack)

Proof-of-concept проекта, демонстрирующего создание потока
с предаллоцированным стеком 5GB:

- Windows x64 — через `CreateThread`
- Linux musl (Alpine) — через `mmap + pthread_attr_setstack`

Внутри создаётся contiguous регион 4.5GB,
который оборачивается в `BigSpan` (byte* + ulong Length)
и позволяет получать обычные `Span<byte>` через `.Slice()`.

---

## 📂 Структура проекта

```
/win
    Program.cs
    stack-poc.csproj

/musl
    Program.cs
    stack-poc.csproj
    dockerfile
```

---

# 🪟 Windows x64

### Что используется

- `CreateThread`
- `STACK_SIZE_PARAM_IS_A_RESERVATION`
- reserve = 5 GiB
- постепенный commit (если нужно)
- `BigSpan + Slice`

### Требования

- .NET 8.0 SDK или выше
- Windows x64

### Сборка

```bash
cd win
dotnet build -c Release
```

### Запуск

```bash
cd win
dotnet run -c Release
```

---

# 🐧 Linux (musl / Alpine)

### Что используется

- `mmap` (5GB)
- `pthread_attr_setstack`
- `pthread_create`
- arena в нижней части стека
- `BigSpan + Slice`
- без stack-grow механизма
- без glibc зависимостей

### Требования

- Docker
- Минимум 20GB RAM для контейнера

---

## 🐳 Docker build & run

```bash
docker build -t stack-poc ./musl
docker run --rm --memory=20g stack-poc
```

---

# 🔥 BigSpan

```csharp
public readonly ref struct BigSpan
{
    readonly byte* _base;
    public readonly ulong Length;

    public BigSpan(byte* b, ulong len)
    {
        _base = b;
        Length = len;
    }

    public ref byte this[ulong index]
    {
        get
        {
            if (index >= Length) throw new IndexOutOfRangeException();
            return ref *(_base + (nuint)index);
        }
    }

    public Span<byte> Slice(ulong offset, int length)
    {
        if (offset > Length) throw new ArgumentOutOfRangeException(nameof(offset));
        if ((ulong)length > Length - offset)
            throw new ArgumentOutOfRangeException(nameof(length));

        return new Span<byte>(_base + (nuint)offset, length);
    }
}
```

---

# ⚠ Ограничения

- `Span<byte>` ограничен `int`
- `Slice()` максимум 2GB за раз
- Docker memory limit должен быть >5GB
- Windows может требовать достаточный commit limit

---

# 🧠 Цель проекта

Исследование:

- больших стеков (>4GB)
- управления стеком вручную
- обхода guard-grow механизма
- создания custom arena на основе стека

Не предназначено для production.

---

# 📜 License

MIT
