using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.Reflection;

/// <summary>
/// Hand-written protobuf-compatible messages for batch/vectorized environment operations.
/// These bypass the proto compiler but are wire-compatible with standard protobuf encoding.
/// </summary>

/// <summary>
/// Request to initialize N vectorized environments.
/// Fields: num_environments (1, int64)
/// </summary>
public sealed class InitEnvironmentsRequest : IMessage<InitEnvironmentsRequest>
{
    public static MessageParser<InitEnvironmentsRequest> Parser { get; } = new MessageParser<InitEnvironmentsRequest>(() => new InitEnvironmentsRequest());

    public long NumEnvironments { get; set; }

    public MessageDescriptor Descriptor => null;

    public void MergeFrom(InitEnvironmentsRequest other)
    {
        if (other == null) return;
        NumEnvironments = other.NumEnvironments;
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (tag)
            {
                case 8: NumEnvironments = input.ReadInt64(); break;
                default: input.SkipLastField(); break;
            }
        }
    }

    public void WriteTo(CodedOutputStream output)
    {
        if (NumEnvironments != 0) { output.WriteTag(1, WireFormat.WireType.Varint); output.WriteInt64(NumEnvironments); }
    }

    public int CalculateSize()
    {
        int size = 0;
        if (NumEnvironments != 0) size += 1 + CodedOutputStream.ComputeInt64Size(NumEnvironments);
        return size;
    }

    public InitEnvironmentsRequest Clone() => new InitEnvironmentsRequest { NumEnvironments = NumEnvironments };

    public bool Equals(InitEnvironmentsRequest other) => other != null && NumEnvironments == other.NumEnvironments;
    public override bool Equals(object obj) => Equals(obj as InitEnvironmentsRequest);
    public override int GetHashCode() => NumEnvironments.GetHashCode();
    public override string ToString() => $"InitEnvironmentsRequest {{ NumEnvironments={NumEnvironments} }}";
}

/// <summary>
/// Request to step all environments with per-env actions.
/// Fields: p1_actions (1, repeated int64 packed), p2_actions (2, repeated int64 packed), n_frames (3, int64)
/// </summary>
public sealed class BatchStepInput : IMessage<BatchStepInput>
{
    public static MessageParser<BatchStepInput> Parser { get; } = new MessageParser<BatchStepInput>(() => new BatchStepInput());

    public RepeatedField<long> P1Actions { get; } = new RepeatedField<long>();
    public RepeatedField<long> P2Actions { get; } = new RepeatedField<long>();
    public long NFrames { get; set; } = 1;

    public MessageDescriptor Descriptor => null;

    public void MergeFrom(BatchStepInput other)
    {
        if (other == null) return;
        P1Actions.Add(other.P1Actions);
        P2Actions.Add(other.P2Actions);
        NFrames = other.NFrames;
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (tag)
            {
                case 10: // packed repeated int64
                case 8:  // individual int64
                    P1Actions.AddEntriesFrom(input, FieldCodec.ForInt64(10));
                    break;
                case 18: // packed repeated int64
                case 16: // individual int64
                    P2Actions.AddEntriesFrom(input, FieldCodec.ForInt64(18));
                    break;
                case 24: NFrames = input.ReadInt64(); break;
                default: input.SkipLastField(); break;
            }
        }
    }

    public void WriteTo(CodedOutputStream output)
    {
        P1Actions.WriteTo(output, FieldCodec.ForInt64(10));
        P2Actions.WriteTo(output, FieldCodec.ForInt64(18));
        if (NFrames != 0) { output.WriteTag(3, WireFormat.WireType.Varint); output.WriteInt64(NFrames); }
    }

    public int CalculateSize()
    {
        int size = 0;
        size += P1Actions.CalculateSize(FieldCodec.ForInt64(10));
        size += P2Actions.CalculateSize(FieldCodec.ForInt64(18));
        if (NFrames != 0) size += 1 + CodedOutputStream.ComputeInt64Size(NFrames);
        return size;
    }

    public BatchStepInput Clone()
    {
        var clone = new BatchStepInput { NFrames = NFrames };
        clone.P1Actions.Add(P1Actions);
        clone.P2Actions.Add(P2Actions);
        return clone;
    }

    public bool Equals(BatchStepInput other) => other != null;
    public override bool Equals(object obj) => Equals(obj as BatchStepInput);
    public override int GetHashCode() => P1Actions.GetHashCode();
    public override string ToString() => $"BatchStepInput {{ envs={P1Actions.Count}, nFrames={NFrames} }}";
}

/// <summary>
/// Request to reset specific environments.
/// Fields: reset_mask (1, repeated bool packed)
/// </summary>
public sealed class BatchResetInput : IMessage<BatchResetInput>
{
    public static MessageParser<BatchResetInput> Parser { get; } = new MessageParser<BatchResetInput>(() => new BatchResetInput());

    public RepeatedField<bool> ResetMask { get; } = new RepeatedField<bool>();

    public MessageDescriptor Descriptor => null;

    public void MergeFrom(BatchResetInput other)
    {
        if (other == null) return;
        ResetMask.Add(other.ResetMask);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (tag)
            {
                case 10: // packed
                case 8:  // individual
                    ResetMask.AddEntriesFrom(input, FieldCodec.ForBool(10));
                    break;
                default: input.SkipLastField(); break;
            }
        }
    }

    public void WriteTo(CodedOutputStream output)
    {
        ResetMask.WriteTo(output, FieldCodec.ForBool(10));
    }

    public int CalculateSize()
    {
        return ResetMask.CalculateSize(FieldCodec.ForBool(10));
    }

    public BatchResetInput Clone()
    {
        var clone = new BatchResetInput();
        clone.ResetMask.Add(ResetMask);
        return clone;
    }

    public bool Equals(BatchResetInput other) => other != null;
    public override bool Equals(object obj) => Equals(obj as BatchResetInput);
    public override int GetHashCode() => ResetMask.GetHashCode();
    public override string ToString() => $"BatchResetInput {{ envs={ResetMask.Count} }}";
}

/// <summary>
/// Response containing encoded states for all environments.
/// Fields: p1_encodings (1, repeated float packed), p2_encodings (2, repeated float packed),
///         round_states (3, repeated int64 packed), dones (4, repeated bool packed), rewards (5, repeated int32 packed)
/// </summary>
public sealed class BatchEncodedState : IMessage<BatchEncodedState>
{
    public static MessageParser<BatchEncodedState> Parser { get; } = new MessageParser<BatchEncodedState>(() => new BatchEncodedState());

    public RepeatedField<float> P1Encodings { get; } = new RepeatedField<float>();
    public RepeatedField<float> P2Encodings { get; } = new RepeatedField<float>();
    public RepeatedField<long> RoundStates { get; } = new RepeatedField<long>();
    public RepeatedField<bool> Dones { get; } = new RepeatedField<bool>();
    public RepeatedField<int> Rewards { get; } = new RepeatedField<int>();

    public MessageDescriptor Descriptor => null;

    public void MergeFrom(BatchEncodedState other)
    {
        if (other == null) return;
        P1Encodings.Add(other.P1Encodings);
        P2Encodings.Add(other.P2Encodings);
        RoundStates.Add(other.RoundStates);
        Dones.Add(other.Dones);
        Rewards.Add(other.Rewards);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (tag)
            {
                case 10: case 13: P1Encodings.AddEntriesFrom(input, FieldCodec.ForFloat(10)); break;
                case 18: case 21: P2Encodings.AddEntriesFrom(input, FieldCodec.ForFloat(18)); break;
                case 26: case 24: RoundStates.AddEntriesFrom(input, FieldCodec.ForInt64(26)); break;
                case 34: case 32: Dones.AddEntriesFrom(input, FieldCodec.ForBool(34)); break;
                case 42: case 40: Rewards.AddEntriesFrom(input, FieldCodec.ForInt32(42)); break;
                default: input.SkipLastField(); break;
            }
        }
    }

    public void WriteTo(CodedOutputStream output)
    {
        P1Encodings.WriteTo(output, FieldCodec.ForFloat(10));
        P2Encodings.WriteTo(output, FieldCodec.ForFloat(18));
        RoundStates.WriteTo(output, FieldCodec.ForInt64(26));
        Dones.WriteTo(output, FieldCodec.ForBool(34));
        Rewards.WriteTo(output, FieldCodec.ForInt32(42));
    }

    public int CalculateSize()
    {
        int size = 0;
        size += P1Encodings.CalculateSize(FieldCodec.ForFloat(10));
        size += P2Encodings.CalculateSize(FieldCodec.ForFloat(18));
        size += RoundStates.CalculateSize(FieldCodec.ForInt64(26));
        size += Dones.CalculateSize(FieldCodec.ForBool(34));
        size += Rewards.CalculateSize(FieldCodec.ForInt32(42));
        return size;
    }

    public BatchEncodedState Clone()
    {
        var clone = new BatchEncodedState();
        clone.P1Encodings.Add(P1Encodings);
        clone.P2Encodings.Add(P2Encodings);
        clone.RoundStates.Add(RoundStates);
        clone.Dones.Add(Dones);
        clone.Rewards.Add(Rewards);
        return clone;
    }

    public bool Equals(BatchEncodedState other) => other != null;
    public override bool Equals(object obj) => Equals(obj as BatchEncodedState);
    public override int GetHashCode() => P1Encodings.GetHashCode();
    public override string ToString() => $"BatchEncodedState {{ p1_size={P1Encodings.Count}, dones={Dones.Count} }}";
}
