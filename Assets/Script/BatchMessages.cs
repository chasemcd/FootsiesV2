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
/// Response containing raw (unencoded) game states for all environments as flat arrays of length num_envs.
/// Used when Python performs its own encoding. Python accesses as: response.p1_position_x[env_idx], etc.
///
/// Field layout:
///   3: round_states (repeated int64)       4: dones (repeated bool)
///   5: rewards (repeated int32)            6: frame_counts (repeated int64)
///   P1 fields (7-26):
///   7:  p1_position_x (float)              8:  p1_is_dead (bool)
///   9:  p1_vital_health (int64)            10: p1_guard_health (int64)
///   11: p1_current_action_id (int64)       12: p1_current_action_frame (int64)
///   13: p1_current_action_frame_count (int64) 14: p1_is_action_end (bool)
///   15: p1_is_always_cancelable (bool)     16: p1_current_action_hit_count (int64)
///   17: p1_current_hit_stun_frame (int64)  18: p1_is_in_hit_stun (bool)
///   19: p1_sprite_shake_position (int64)   20: p1_max_sprite_shake_frame (int64)
///   21: p1_velocity_x (float)             22: p1_is_face_right (bool)
///   23: p1_current_frame_advantage (int64) 24: p1_would_next_forward_input_dash (bool)
///   25: p1_would_next_backward_input_dash (bool) 26: p1_special_attack_progress (float)
///   P2 fields (27-46): same layout as P1
/// </summary>
public sealed class BatchRawState : IMessage<BatchRawState>
{
    public static MessageParser<BatchRawState> Parser { get; } = new MessageParser<BatchRawState>(() => new BatchRawState());

    // Common fields
    public RepeatedField<long> RoundStates { get; } = new RepeatedField<long>();           // field 3
    public RepeatedField<bool> Dones { get; } = new RepeatedField<bool>();                 // field 4
    public RepeatedField<int> Rewards { get; } = new RepeatedField<int>();                 // field 5
    public RepeatedField<long> FrameCounts { get; } = new RepeatedField<long>();            // field 6

    // P1 state arrays (each length num_envs)
    public RepeatedField<float> P1PositionX { get; } = new RepeatedField<float>();          // field 7
    public RepeatedField<bool> P1IsDead { get; } = new RepeatedField<bool>();               // field 8
    public RepeatedField<long> P1VitalHealth { get; } = new RepeatedField<long>();          // field 9
    public RepeatedField<long> P1GuardHealth { get; } = new RepeatedField<long>();          // field 10
    public RepeatedField<long> P1CurrentActionId { get; } = new RepeatedField<long>();      // field 11
    public RepeatedField<long> P1CurrentActionFrame { get; } = new RepeatedField<long>();   // field 12
    public RepeatedField<long> P1CurrentActionFrameCount { get; } = new RepeatedField<long>(); // field 13
    public RepeatedField<bool> P1IsActionEnd { get; } = new RepeatedField<bool>();          // field 14
    public RepeatedField<bool> P1IsAlwaysCancelable { get; } = new RepeatedField<bool>();   // field 15
    public RepeatedField<long> P1CurrentActionHitCount { get; } = new RepeatedField<long>();// field 16
    public RepeatedField<long> P1CurrentHitStunFrame { get; } = new RepeatedField<long>();  // field 17
    public RepeatedField<bool> P1IsInHitStun { get; } = new RepeatedField<bool>();          // field 18
    public RepeatedField<long> P1SpriteShakePosition { get; } = new RepeatedField<long>();  // field 19
    public RepeatedField<long> P1MaxSpriteShakeFrame { get; } = new RepeatedField<long>();  // field 20
    public RepeatedField<float> P1VelocityX { get; } = new RepeatedField<float>();          // field 21
    public RepeatedField<bool> P1IsFaceRight { get; } = new RepeatedField<bool>();          // field 22
    public RepeatedField<long> P1CurrentFrameAdvantage { get; } = new RepeatedField<long>();// field 23
    public RepeatedField<bool> P1WouldNextForwardInputDash { get; } = new RepeatedField<bool>(); // field 24
    public RepeatedField<bool> P1WouldNextBackwardInputDash { get; } = new RepeatedField<bool>(); // field 25
    public RepeatedField<float> P1SpecialAttackProgress { get; } = new RepeatedField<float>(); // field 26

    // P2 state arrays (each length num_envs)
    public RepeatedField<float> P2PositionX { get; } = new RepeatedField<float>();          // field 27
    public RepeatedField<bool> P2IsDead { get; } = new RepeatedField<bool>();               // field 28
    public RepeatedField<long> P2VitalHealth { get; } = new RepeatedField<long>();          // field 29
    public RepeatedField<long> P2GuardHealth { get; } = new RepeatedField<long>();          // field 30
    public RepeatedField<long> P2CurrentActionId { get; } = new RepeatedField<long>();      // field 31
    public RepeatedField<long> P2CurrentActionFrame { get; } = new RepeatedField<long>();   // field 32
    public RepeatedField<long> P2CurrentActionFrameCount { get; } = new RepeatedField<long>(); // field 33
    public RepeatedField<bool> P2IsActionEnd { get; } = new RepeatedField<bool>();          // field 34
    public RepeatedField<bool> P2IsAlwaysCancelable { get; } = new RepeatedField<bool>();   // field 35
    public RepeatedField<long> P2CurrentActionHitCount { get; } = new RepeatedField<long>();// field 36
    public RepeatedField<long> P2CurrentHitStunFrame { get; } = new RepeatedField<long>();  // field 37
    public RepeatedField<bool> P2IsInHitStun { get; } = new RepeatedField<bool>();          // field 38
    public RepeatedField<long> P2SpriteShakePosition { get; } = new RepeatedField<long>();  // field 39
    public RepeatedField<long> P2MaxSpriteShakeFrame { get; } = new RepeatedField<long>();  // field 40
    public RepeatedField<float> P2VelocityX { get; } = new RepeatedField<float>();          // field 41
    public RepeatedField<bool> P2IsFaceRight { get; } = new RepeatedField<bool>();          // field 42
    public RepeatedField<long> P2CurrentFrameAdvantage { get; } = new RepeatedField<long>();// field 43
    public RepeatedField<bool> P2WouldNextForwardInputDash { get; } = new RepeatedField<bool>(); // field 44
    public RepeatedField<bool> P2WouldNextBackwardInputDash { get; } = new RepeatedField<bool>(); // field 45
    public RepeatedField<float> P2SpecialAttackProgress { get; } = new RepeatedField<float>(); // field 46

    // FieldCodecs keyed by packed tag = (field_number << 3) | 2
    // For MergeFrom, we also match individual element tags:
    //   varint (int64/int32/bool): (field_number << 3) | 0
    //   fixed32 (float):          (field_number << 3) | 5

    public MessageDescriptor Descriptor => null;

    public void MergeFrom(BatchRawState other)
    {
        if (other == null) return;
        RoundStates.Add(other.RoundStates);
        Dones.Add(other.Dones);
        Rewards.Add(other.Rewards);
        FrameCounts.Add(other.FrameCounts);
        P1PositionX.Add(other.P1PositionX); P1IsDead.Add(other.P1IsDead);
        P1VitalHealth.Add(other.P1VitalHealth); P1GuardHealth.Add(other.P1GuardHealth);
        P1CurrentActionId.Add(other.P1CurrentActionId); P1CurrentActionFrame.Add(other.P1CurrentActionFrame);
        P1CurrentActionFrameCount.Add(other.P1CurrentActionFrameCount); P1IsActionEnd.Add(other.P1IsActionEnd);
        P1IsAlwaysCancelable.Add(other.P1IsAlwaysCancelable); P1CurrentActionHitCount.Add(other.P1CurrentActionHitCount);
        P1CurrentHitStunFrame.Add(other.P1CurrentHitStunFrame); P1IsInHitStun.Add(other.P1IsInHitStun);
        P1SpriteShakePosition.Add(other.P1SpriteShakePosition); P1MaxSpriteShakeFrame.Add(other.P1MaxSpriteShakeFrame);
        P1VelocityX.Add(other.P1VelocityX); P1IsFaceRight.Add(other.P1IsFaceRight);
        P1CurrentFrameAdvantage.Add(other.P1CurrentFrameAdvantage);
        P1WouldNextForwardInputDash.Add(other.P1WouldNextForwardInputDash);
        P1WouldNextBackwardInputDash.Add(other.P1WouldNextBackwardInputDash);
        P1SpecialAttackProgress.Add(other.P1SpecialAttackProgress);
        P2PositionX.Add(other.P2PositionX); P2IsDead.Add(other.P2IsDead);
        P2VitalHealth.Add(other.P2VitalHealth); P2GuardHealth.Add(other.P2GuardHealth);
        P2CurrentActionId.Add(other.P2CurrentActionId); P2CurrentActionFrame.Add(other.P2CurrentActionFrame);
        P2CurrentActionFrameCount.Add(other.P2CurrentActionFrameCount); P2IsActionEnd.Add(other.P2IsActionEnd);
        P2IsAlwaysCancelable.Add(other.P2IsAlwaysCancelable); P2CurrentActionHitCount.Add(other.P2CurrentActionHitCount);
        P2CurrentHitStunFrame.Add(other.P2CurrentHitStunFrame); P2IsInHitStun.Add(other.P2IsInHitStun);
        P2SpriteShakePosition.Add(other.P2SpriteShakePosition); P2MaxSpriteShakeFrame.Add(other.P2MaxSpriteShakeFrame);
        P2VelocityX.Add(other.P2VelocityX); P2IsFaceRight.Add(other.P2IsFaceRight);
        P2CurrentFrameAdvantage.Add(other.P2CurrentFrameAdvantage);
        P2WouldNextForwardInputDash.Add(other.P2WouldNextForwardInputDash);
        P2WouldNextBackwardInputDash.Add(other.P2WouldNextBackwardInputDash);
        P2SpecialAttackProgress.Add(other.P2SpecialAttackProgress);
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (tag)
            {
                // Common: RoundStates(3), Dones(4), Rewards(5), FrameCounts(6)
                case 26: case 24: RoundStates.AddEntriesFrom(input, FieldCodec.ForInt64(26)); break;
                case 34: case 32: Dones.AddEntriesFrom(input, FieldCodec.ForBool(34)); break;
                case 42: case 40: Rewards.AddEntriesFrom(input, FieldCodec.ForInt32(42)); break;
                case 50: case 48: FrameCounts.AddEntriesFrom(input, FieldCodec.ForInt64(50)); break;
                // P1 fields (7-26)
                case 58:  case 61:  P1PositionX.AddEntriesFrom(input, FieldCodec.ForFloat(58)); break;
                case 66:  case 64:  P1IsDead.AddEntriesFrom(input, FieldCodec.ForBool(66)); break;
                case 74:  case 72:  P1VitalHealth.AddEntriesFrom(input, FieldCodec.ForInt64(74)); break;
                case 82:  case 80:  P1GuardHealth.AddEntriesFrom(input, FieldCodec.ForInt64(82)); break;
                case 90:  case 88:  P1CurrentActionId.AddEntriesFrom(input, FieldCodec.ForInt64(90)); break;
                case 98:  case 96:  P1CurrentActionFrame.AddEntriesFrom(input, FieldCodec.ForInt64(98)); break;
                case 106: case 104: P1CurrentActionFrameCount.AddEntriesFrom(input, FieldCodec.ForInt64(106)); break;
                case 114: case 112: P1IsActionEnd.AddEntriesFrom(input, FieldCodec.ForBool(114)); break;
                case 122: case 120: P1IsAlwaysCancelable.AddEntriesFrom(input, FieldCodec.ForBool(122)); break;
                case 130: case 128: P1CurrentActionHitCount.AddEntriesFrom(input, FieldCodec.ForInt64(130)); break;
                case 138: case 136: P1CurrentHitStunFrame.AddEntriesFrom(input, FieldCodec.ForInt64(138)); break;
                case 146: case 144: P1IsInHitStun.AddEntriesFrom(input, FieldCodec.ForBool(146)); break;
                case 154: case 152: P1SpriteShakePosition.AddEntriesFrom(input, FieldCodec.ForInt64(154)); break;
                case 162: case 160: P1MaxSpriteShakeFrame.AddEntriesFrom(input, FieldCodec.ForInt64(162)); break;
                case 170: case 173: P1VelocityX.AddEntriesFrom(input, FieldCodec.ForFloat(170)); break;
                case 178: case 176: P1IsFaceRight.AddEntriesFrom(input, FieldCodec.ForBool(178)); break;
                case 186: case 184: P1CurrentFrameAdvantage.AddEntriesFrom(input, FieldCodec.ForInt64(186)); break;
                case 194: case 192: P1WouldNextForwardInputDash.AddEntriesFrom(input, FieldCodec.ForBool(194)); break;
                case 202: case 200: P1WouldNextBackwardInputDash.AddEntriesFrom(input, FieldCodec.ForBool(202)); break;
                case 210: case 213: P1SpecialAttackProgress.AddEntriesFrom(input, FieldCodec.ForFloat(210)); break;
                // P2 fields (27-46)
                case 218: case 221: P2PositionX.AddEntriesFrom(input, FieldCodec.ForFloat(218)); break;
                case 226: case 224: P2IsDead.AddEntriesFrom(input, FieldCodec.ForBool(226)); break;
                case 234: case 232: P2VitalHealth.AddEntriesFrom(input, FieldCodec.ForInt64(234)); break;
                case 242: case 240: P2GuardHealth.AddEntriesFrom(input, FieldCodec.ForInt64(242)); break;
                case 250: case 248: P2CurrentActionId.AddEntriesFrom(input, FieldCodec.ForInt64(250)); break;
                case 258: case 256: P2CurrentActionFrame.AddEntriesFrom(input, FieldCodec.ForInt64(258)); break;
                case 266: case 264: P2CurrentActionFrameCount.AddEntriesFrom(input, FieldCodec.ForInt64(266)); break;
                case 274: case 272: P2IsActionEnd.AddEntriesFrom(input, FieldCodec.ForBool(274)); break;
                case 282: case 280: P2IsAlwaysCancelable.AddEntriesFrom(input, FieldCodec.ForBool(282)); break;
                case 290: case 288: P2CurrentActionHitCount.AddEntriesFrom(input, FieldCodec.ForInt64(290)); break;
                case 298: case 296: P2CurrentHitStunFrame.AddEntriesFrom(input, FieldCodec.ForInt64(298)); break;
                case 306: case 304: P2IsInHitStun.AddEntriesFrom(input, FieldCodec.ForBool(306)); break;
                case 314: case 312: P2SpriteShakePosition.AddEntriesFrom(input, FieldCodec.ForInt64(314)); break;
                case 322: case 320: P2MaxSpriteShakeFrame.AddEntriesFrom(input, FieldCodec.ForInt64(322)); break;
                case 330: case 333: P2VelocityX.AddEntriesFrom(input, FieldCodec.ForFloat(330)); break;
                case 338: case 336: P2IsFaceRight.AddEntriesFrom(input, FieldCodec.ForBool(338)); break;
                case 346: case 344: P2CurrentFrameAdvantage.AddEntriesFrom(input, FieldCodec.ForInt64(346)); break;
                case 354: case 352: P2WouldNextForwardInputDash.AddEntriesFrom(input, FieldCodec.ForBool(354)); break;
                case 362: case 360: P2WouldNextBackwardInputDash.AddEntriesFrom(input, FieldCodec.ForBool(362)); break;
                case 370: case 373: P2SpecialAttackProgress.AddEntriesFrom(input, FieldCodec.ForFloat(370)); break;
                default: input.SkipLastField(); break;
            }
        }
    }

    public void WriteTo(CodedOutputStream output)
    {
        // Common
        RoundStates.WriteTo(output, FieldCodec.ForInt64(26));
        Dones.WriteTo(output, FieldCodec.ForBool(34));
        Rewards.WriteTo(output, FieldCodec.ForInt32(42));
        FrameCounts.WriteTo(output, FieldCodec.ForInt64(50));
        // P1
        P1PositionX.WriteTo(output, FieldCodec.ForFloat(58));
        P1IsDead.WriteTo(output, FieldCodec.ForBool(66));
        P1VitalHealth.WriteTo(output, FieldCodec.ForInt64(74));
        P1GuardHealth.WriteTo(output, FieldCodec.ForInt64(82));
        P1CurrentActionId.WriteTo(output, FieldCodec.ForInt64(90));
        P1CurrentActionFrame.WriteTo(output, FieldCodec.ForInt64(98));
        P1CurrentActionFrameCount.WriteTo(output, FieldCodec.ForInt64(106));
        P1IsActionEnd.WriteTo(output, FieldCodec.ForBool(114));
        P1IsAlwaysCancelable.WriteTo(output, FieldCodec.ForBool(122));
        P1CurrentActionHitCount.WriteTo(output, FieldCodec.ForInt64(130));
        P1CurrentHitStunFrame.WriteTo(output, FieldCodec.ForInt64(138));
        P1IsInHitStun.WriteTo(output, FieldCodec.ForBool(146));
        P1SpriteShakePosition.WriteTo(output, FieldCodec.ForInt64(154));
        P1MaxSpriteShakeFrame.WriteTo(output, FieldCodec.ForInt64(162));
        P1VelocityX.WriteTo(output, FieldCodec.ForFloat(170));
        P1IsFaceRight.WriteTo(output, FieldCodec.ForBool(178));
        P1CurrentFrameAdvantage.WriteTo(output, FieldCodec.ForInt64(186));
        P1WouldNextForwardInputDash.WriteTo(output, FieldCodec.ForBool(194));
        P1WouldNextBackwardInputDash.WriteTo(output, FieldCodec.ForBool(202));
        P1SpecialAttackProgress.WriteTo(output, FieldCodec.ForFloat(210));
        // P2
        P2PositionX.WriteTo(output, FieldCodec.ForFloat(218));
        P2IsDead.WriteTo(output, FieldCodec.ForBool(226));
        P2VitalHealth.WriteTo(output, FieldCodec.ForInt64(234));
        P2GuardHealth.WriteTo(output, FieldCodec.ForInt64(242));
        P2CurrentActionId.WriteTo(output, FieldCodec.ForInt64(250));
        P2CurrentActionFrame.WriteTo(output, FieldCodec.ForInt64(258));
        P2CurrentActionFrameCount.WriteTo(output, FieldCodec.ForInt64(266));
        P2IsActionEnd.WriteTo(output, FieldCodec.ForBool(274));
        P2IsAlwaysCancelable.WriteTo(output, FieldCodec.ForBool(282));
        P2CurrentActionHitCount.WriteTo(output, FieldCodec.ForInt64(290));
        P2CurrentHitStunFrame.WriteTo(output, FieldCodec.ForInt64(298));
        P2IsInHitStun.WriteTo(output, FieldCodec.ForBool(306));
        P2SpriteShakePosition.WriteTo(output, FieldCodec.ForInt64(314));
        P2MaxSpriteShakeFrame.WriteTo(output, FieldCodec.ForInt64(322));
        P2VelocityX.WriteTo(output, FieldCodec.ForFloat(330));
        P2IsFaceRight.WriteTo(output, FieldCodec.ForBool(338));
        P2CurrentFrameAdvantage.WriteTo(output, FieldCodec.ForInt64(346));
        P2WouldNextForwardInputDash.WriteTo(output, FieldCodec.ForBool(354));
        P2WouldNextBackwardInputDash.WriteTo(output, FieldCodec.ForBool(362));
        P2SpecialAttackProgress.WriteTo(output, FieldCodec.ForFloat(370));
    }

    public int CalculateSize()
    {
        int size = 0;
        // Common
        size += RoundStates.CalculateSize(FieldCodec.ForInt64(26));
        size += Dones.CalculateSize(FieldCodec.ForBool(34));
        size += Rewards.CalculateSize(FieldCodec.ForInt32(42));
        size += FrameCounts.CalculateSize(FieldCodec.ForInt64(50));
        // P1
        size += P1PositionX.CalculateSize(FieldCodec.ForFloat(58));
        size += P1IsDead.CalculateSize(FieldCodec.ForBool(66));
        size += P1VitalHealth.CalculateSize(FieldCodec.ForInt64(74));
        size += P1GuardHealth.CalculateSize(FieldCodec.ForInt64(82));
        size += P1CurrentActionId.CalculateSize(FieldCodec.ForInt64(90));
        size += P1CurrentActionFrame.CalculateSize(FieldCodec.ForInt64(98));
        size += P1CurrentActionFrameCount.CalculateSize(FieldCodec.ForInt64(106));
        size += P1IsActionEnd.CalculateSize(FieldCodec.ForBool(114));
        size += P1IsAlwaysCancelable.CalculateSize(FieldCodec.ForBool(122));
        size += P1CurrentActionHitCount.CalculateSize(FieldCodec.ForInt64(130));
        size += P1CurrentHitStunFrame.CalculateSize(FieldCodec.ForInt64(138));
        size += P1IsInHitStun.CalculateSize(FieldCodec.ForBool(146));
        size += P1SpriteShakePosition.CalculateSize(FieldCodec.ForInt64(154));
        size += P1MaxSpriteShakeFrame.CalculateSize(FieldCodec.ForInt64(162));
        size += P1VelocityX.CalculateSize(FieldCodec.ForFloat(170));
        size += P1IsFaceRight.CalculateSize(FieldCodec.ForBool(178));
        size += P1CurrentFrameAdvantage.CalculateSize(FieldCodec.ForInt64(186));
        size += P1WouldNextForwardInputDash.CalculateSize(FieldCodec.ForBool(194));
        size += P1WouldNextBackwardInputDash.CalculateSize(FieldCodec.ForBool(202));
        size += P1SpecialAttackProgress.CalculateSize(FieldCodec.ForFloat(210));
        // P2
        size += P2PositionX.CalculateSize(FieldCodec.ForFloat(218));
        size += P2IsDead.CalculateSize(FieldCodec.ForBool(226));
        size += P2VitalHealth.CalculateSize(FieldCodec.ForInt64(234));
        size += P2GuardHealth.CalculateSize(FieldCodec.ForInt64(242));
        size += P2CurrentActionId.CalculateSize(FieldCodec.ForInt64(250));
        size += P2CurrentActionFrame.CalculateSize(FieldCodec.ForInt64(258));
        size += P2CurrentActionFrameCount.CalculateSize(FieldCodec.ForInt64(266));
        size += P2IsActionEnd.CalculateSize(FieldCodec.ForBool(274));
        size += P2IsAlwaysCancelable.CalculateSize(FieldCodec.ForBool(282));
        size += P2CurrentActionHitCount.CalculateSize(FieldCodec.ForInt64(290));
        size += P2CurrentHitStunFrame.CalculateSize(FieldCodec.ForInt64(298));
        size += P2IsInHitStun.CalculateSize(FieldCodec.ForBool(306));
        size += P2SpriteShakePosition.CalculateSize(FieldCodec.ForInt64(314));
        size += P2MaxSpriteShakeFrame.CalculateSize(FieldCodec.ForInt64(322));
        size += P2VelocityX.CalculateSize(FieldCodec.ForFloat(330));
        size += P2IsFaceRight.CalculateSize(FieldCodec.ForBool(338));
        size += P2CurrentFrameAdvantage.CalculateSize(FieldCodec.ForInt64(346));
        size += P2WouldNextForwardInputDash.CalculateSize(FieldCodec.ForBool(354));
        size += P2WouldNextBackwardInputDash.CalculateSize(FieldCodec.ForBool(362));
        size += P2SpecialAttackProgress.CalculateSize(FieldCodec.ForFloat(370));
        return size;
    }

    public BatchRawState Clone()
    {
        var clone = new BatchRawState();
        clone.RoundStates.Add(RoundStates); clone.Dones.Add(Dones);
        clone.Rewards.Add(Rewards); clone.FrameCounts.Add(FrameCounts);
        clone.P1PositionX.Add(P1PositionX); clone.P1IsDead.Add(P1IsDead);
        clone.P1VitalHealth.Add(P1VitalHealth); clone.P1GuardHealth.Add(P1GuardHealth);
        clone.P1CurrentActionId.Add(P1CurrentActionId); clone.P1CurrentActionFrame.Add(P1CurrentActionFrame);
        clone.P1CurrentActionFrameCount.Add(P1CurrentActionFrameCount); clone.P1IsActionEnd.Add(P1IsActionEnd);
        clone.P1IsAlwaysCancelable.Add(P1IsAlwaysCancelable); clone.P1CurrentActionHitCount.Add(P1CurrentActionHitCount);
        clone.P1CurrentHitStunFrame.Add(P1CurrentHitStunFrame); clone.P1IsInHitStun.Add(P1IsInHitStun);
        clone.P1SpriteShakePosition.Add(P1SpriteShakePosition); clone.P1MaxSpriteShakeFrame.Add(P1MaxSpriteShakeFrame);
        clone.P1VelocityX.Add(P1VelocityX); clone.P1IsFaceRight.Add(P1IsFaceRight);
        clone.P1CurrentFrameAdvantage.Add(P1CurrentFrameAdvantage);
        clone.P1WouldNextForwardInputDash.Add(P1WouldNextForwardInputDash);
        clone.P1WouldNextBackwardInputDash.Add(P1WouldNextBackwardInputDash);
        clone.P1SpecialAttackProgress.Add(P1SpecialAttackProgress);
        clone.P2PositionX.Add(P2PositionX); clone.P2IsDead.Add(P2IsDead);
        clone.P2VitalHealth.Add(P2VitalHealth); clone.P2GuardHealth.Add(P2GuardHealth);
        clone.P2CurrentActionId.Add(P2CurrentActionId); clone.P2CurrentActionFrame.Add(P2CurrentActionFrame);
        clone.P2CurrentActionFrameCount.Add(P2CurrentActionFrameCount); clone.P2IsActionEnd.Add(P2IsActionEnd);
        clone.P2IsAlwaysCancelable.Add(P2IsAlwaysCancelable); clone.P2CurrentActionHitCount.Add(P2CurrentActionHitCount);
        clone.P2CurrentHitStunFrame.Add(P2CurrentHitStunFrame); clone.P2IsInHitStun.Add(P2IsInHitStun);
        clone.P2SpriteShakePosition.Add(P2SpriteShakePosition); clone.P2MaxSpriteShakeFrame.Add(P2MaxSpriteShakeFrame);
        clone.P2VelocityX.Add(P2VelocityX); clone.P2IsFaceRight.Add(P2IsFaceRight);
        clone.P2CurrentFrameAdvantage.Add(P2CurrentFrameAdvantage);
        clone.P2WouldNextForwardInputDash.Add(P2WouldNextForwardInputDash);
        clone.P2WouldNextBackwardInputDash.Add(P2WouldNextBackwardInputDash);
        clone.P2SpecialAttackProgress.Add(P2SpecialAttackProgress);
        return clone;
    }

    public bool Equals(BatchRawState other) => other != null;
    public override bool Equals(object obj) => Equals(obj as BatchRawState);
    public override int GetHashCode() => RoundStates.GetHashCode();
    public override string ToString() => $"BatchRawState {{ envs={Dones.Count} }}";
}

/// <summary>
/// Response containing pre-encoded observations for all environments.
/// p1_encodings and p2_encodings are flat float arrays of length num_envs * obs_size.
/// Python reshapes as: np.array(response.p1_encodings).reshape(num_envs, obs_size)
///
/// Field layout:
///   1: p1_encodings (repeated float, packed)
///   2: p2_encodings (repeated float, packed)
///   3: round_states (repeated int64)
///   4: dones (repeated bool)
///   5: rewards (repeated int32)
/// </summary>
public sealed class BatchEncodedState : IMessage<BatchEncodedState>
{
    public static MessageParser<BatchEncodedState> Parser { get; } = new MessageParser<BatchEncodedState>(() => new BatchEncodedState());

    public RepeatedField<float> P1Encodings { get; } = new RepeatedField<float>();   // field 1
    public RepeatedField<float> P2Encodings { get; } = new RepeatedField<float>();   // field 2
    public RepeatedField<long> RoundStates { get; } = new RepeatedField<long>();     // field 3
    public RepeatedField<bool> Dones { get; } = new RepeatedField<bool>();           // field 4
    public RepeatedField<int> Rewards { get; } = new RepeatedField<int>();           // field 5

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
    public override string ToString() => $"BatchEncodedState {{ envs={Dones.Count} }}";
}

/// <summary>
/// Request to step all environments with per-env actions, including Python-side state
/// needed for C#-side encoding (previous actions, special charge state).
///
/// Field layout:
///   1: p1_actions (repeated int64)     2: p2_actions (repeated int64)
///   3: n_frames (int64)               4: prev_p1_actions (repeated int64)
///   5: prev_p2_actions (repeated int64) 6: p1_holding_special (repeated bool)
///   7: p2_holding_special (repeated bool) 8: num_actions (int64)
/// </summary>
public sealed class BatchStepEncodedInput : IMessage<BatchStepEncodedInput>
{
    public static MessageParser<BatchStepEncodedInput> Parser { get; } = new MessageParser<BatchStepEncodedInput>(() => new BatchStepEncodedInput());

    public RepeatedField<long> P1Actions { get; } = new RepeatedField<long>();           // field 1
    public RepeatedField<long> P2Actions { get; } = new RepeatedField<long>();           // field 2
    public long NFrames { get; set; } = 1;                                               // field 3
    public RepeatedField<long> PrevP1Actions { get; } = new RepeatedField<long>();       // field 4
    public RepeatedField<long> PrevP2Actions { get; } = new RepeatedField<long>();       // field 5
    public RepeatedField<bool> P1HoldingSpecial { get; } = new RepeatedField<bool>();    // field 6
    public RepeatedField<bool> P2HoldingSpecial { get; } = new RepeatedField<bool>();    // field 7
    public long NumActions { get; set; }                                                  // field 8

    public MessageDescriptor Descriptor => null;

    public void MergeFrom(BatchStepEncodedInput other)
    {
        if (other == null) return;
        P1Actions.Add(other.P1Actions);
        P2Actions.Add(other.P2Actions);
        NFrames = other.NFrames;
        PrevP1Actions.Add(other.PrevP1Actions);
        PrevP2Actions.Add(other.PrevP2Actions);
        P1HoldingSpecial.Add(other.P1HoldingSpecial);
        P2HoldingSpecial.Add(other.P2HoldingSpecial);
        NumActions = other.NumActions;
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (tag)
            {
                case 10: case 8:  P1Actions.AddEntriesFrom(input, FieldCodec.ForInt64(10)); break;
                case 18: case 16: P2Actions.AddEntriesFrom(input, FieldCodec.ForInt64(18)); break;
                case 24: NFrames = input.ReadInt64(); break;
                case 34: case 32: PrevP1Actions.AddEntriesFrom(input, FieldCodec.ForInt64(34)); break;
                case 42: case 40: PrevP2Actions.AddEntriesFrom(input, FieldCodec.ForInt64(42)); break;
                case 50: case 48: P1HoldingSpecial.AddEntriesFrom(input, FieldCodec.ForBool(50)); break;
                case 58: case 56: P2HoldingSpecial.AddEntriesFrom(input, FieldCodec.ForBool(58)); break;
                case 64: NumActions = input.ReadInt64(); break;
                default: input.SkipLastField(); break;
            }
        }
    }

    public void WriteTo(CodedOutputStream output)
    {
        P1Actions.WriteTo(output, FieldCodec.ForInt64(10));
        P2Actions.WriteTo(output, FieldCodec.ForInt64(18));
        if (NFrames != 0) { output.WriteTag(3, WireFormat.WireType.Varint); output.WriteInt64(NFrames); }
        PrevP1Actions.WriteTo(output, FieldCodec.ForInt64(34));
        PrevP2Actions.WriteTo(output, FieldCodec.ForInt64(42));
        P1HoldingSpecial.WriteTo(output, FieldCodec.ForBool(50));
        P2HoldingSpecial.WriteTo(output, FieldCodec.ForBool(58));
        if (NumActions != 0) { output.WriteTag(8, WireFormat.WireType.Varint); output.WriteInt64(NumActions); }
    }

    public int CalculateSize()
    {
        int size = 0;
        size += P1Actions.CalculateSize(FieldCodec.ForInt64(10));
        size += P2Actions.CalculateSize(FieldCodec.ForInt64(18));
        if (NFrames != 0) size += 1 + CodedOutputStream.ComputeInt64Size(NFrames);
        size += PrevP1Actions.CalculateSize(FieldCodec.ForInt64(34));
        size += PrevP2Actions.CalculateSize(FieldCodec.ForInt64(42));
        size += P1HoldingSpecial.CalculateSize(FieldCodec.ForBool(50));
        size += P2HoldingSpecial.CalculateSize(FieldCodec.ForBool(58));
        if (NumActions != 0) size += 1 + CodedOutputStream.ComputeInt64Size(NumActions);
        return size;
    }

    public BatchStepEncodedInput Clone()
    {
        var clone = new BatchStepEncodedInput { NFrames = NFrames, NumActions = NumActions };
        clone.P1Actions.Add(P1Actions); clone.P2Actions.Add(P2Actions);
        clone.PrevP1Actions.Add(PrevP1Actions); clone.PrevP2Actions.Add(PrevP2Actions);
        clone.P1HoldingSpecial.Add(P1HoldingSpecial); clone.P2HoldingSpecial.Add(P2HoldingSpecial);
        return clone;
    }

    public bool Equals(BatchStepEncodedInput other) => other != null;
    public override bool Equals(object obj) => Equals(obj as BatchStepEncodedInput);
    public override int GetHashCode() => P1Actions.GetHashCode();
    public override string ToString() => $"BatchStepEncodedInput {{ envs={P1Actions.Count}, nFrames={NFrames} }}";
}

/// <summary>
/// Request to reset specific environments and return encoded observations.
/// Fields: reset_mask (1, repeated bool packed), num_actions (2, int64)
/// </summary>
public sealed class BatchResetEncodedInput : IMessage<BatchResetEncodedInput>
{
    public static MessageParser<BatchResetEncodedInput> Parser { get; } = new MessageParser<BatchResetEncodedInput>(() => new BatchResetEncodedInput());

    public RepeatedField<bool> ResetMask { get; } = new RepeatedField<bool>();  // field 1
    public long NumActions { get; set; }                                         // field 2

    public MessageDescriptor Descriptor => null;

    public void MergeFrom(BatchResetEncodedInput other)
    {
        if (other == null) return;
        ResetMask.Add(other.ResetMask);
        NumActions = other.NumActions;
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (tag)
            {
                case 10: case 8: ResetMask.AddEntriesFrom(input, FieldCodec.ForBool(10)); break;
                case 16: NumActions = input.ReadInt64(); break;
                default: input.SkipLastField(); break;
            }
        }
    }

    public void WriteTo(CodedOutputStream output)
    {
        ResetMask.WriteTo(output, FieldCodec.ForBool(10));
        if (NumActions != 0) { output.WriteTag(2, WireFormat.WireType.Varint); output.WriteInt64(NumActions); }
    }

    public int CalculateSize()
    {
        int size = ResetMask.CalculateSize(FieldCodec.ForBool(10));
        if (NumActions != 0) size += 1 + CodedOutputStream.ComputeInt64Size(NumActions);
        return size;
    }

    public BatchResetEncodedInput Clone()
    {
        var clone = new BatchResetEncodedInput { NumActions = NumActions };
        clone.ResetMask.Add(ResetMask);
        return clone;
    }

    public bool Equals(BatchResetEncodedInput other) => other != null;
    public override bool Equals(object obj) => Equals(obj as BatchResetEncodedInput);
    public override int GetHashCode() => ResetMask.GetHashCode();
    public override string ToString() => $"BatchResetEncodedInput {{ envs={ResetMask.Count} }}";
}

/// <summary>
/// Request to reset all environments and return encoded observations.
/// Fields: num_actions (1, int64)
/// </summary>
public sealed class BatchResetAllEncodedInput : IMessage<BatchResetAllEncodedInput>
{
    public static MessageParser<BatchResetAllEncodedInput> Parser { get; } = new MessageParser<BatchResetAllEncodedInput>(() => new BatchResetAllEncodedInput());

    public long NumActions { get; set; }  // field 1

    public MessageDescriptor Descriptor => null;

    public void MergeFrom(BatchResetAllEncodedInput other)
    {
        if (other == null) return;
        NumActions = other.NumActions;
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (tag)
            {
                case 8: NumActions = input.ReadInt64(); break;
                default: input.SkipLastField(); break;
            }
        }
    }

    public void WriteTo(CodedOutputStream output)
    {
        if (NumActions != 0) { output.WriteTag(1, WireFormat.WireType.Varint); output.WriteInt64(NumActions); }
    }

    public int CalculateSize()
    {
        int size = 0;
        if (NumActions != 0) size += 1 + CodedOutputStream.ComputeInt64Size(NumActions);
        return size;
    }

    public BatchResetAllEncodedInput Clone() => new BatchResetAllEncodedInput { NumActions = NumActions };

    public bool Equals(BatchResetAllEncodedInput other) => other != null && NumActions == other.NumActions;
    public override bool Equals(object obj) => Equals(obj as BatchResetAllEncodedInput);
    public override int GetHashCode() => NumActions.GetHashCode();
    public override string ToString() => $"BatchResetAllEncodedInput {{ NumActions={NumActions} }}";
}

/// <summary>
/// Request to encode the current state of all environments without stepping.
/// Provides Python-side encoding context (prev actions, special charge state).
///
/// Field layout:
///   1: prev_p1_actions (repeated int64)    2: prev_p2_actions (repeated int64)
///   3: p1_holding_special (repeated bool)  4: p2_holding_special (repeated bool)
///   5: num_actions (int64)
/// </summary>
public sealed class GetBatchEncodedStateInput : IMessage<GetBatchEncodedStateInput>
{
    public static MessageParser<GetBatchEncodedStateInput> Parser { get; } = new MessageParser<GetBatchEncodedStateInput>(() => new GetBatchEncodedStateInput());

    public RepeatedField<long> PrevP1Actions { get; } = new RepeatedField<long>();       // field 1
    public RepeatedField<long> PrevP2Actions { get; } = new RepeatedField<long>();       // field 2
    public RepeatedField<bool> P1HoldingSpecial { get; } = new RepeatedField<bool>();    // field 3
    public RepeatedField<bool> P2HoldingSpecial { get; } = new RepeatedField<bool>();    // field 4
    public long NumActions { get; set; }                                                  // field 5

    public MessageDescriptor Descriptor => null;

    public void MergeFrom(GetBatchEncodedStateInput other)
    {
        if (other == null) return;
        PrevP1Actions.Add(other.PrevP1Actions);
        PrevP2Actions.Add(other.PrevP2Actions);
        P1HoldingSpecial.Add(other.P1HoldingSpecial);
        P2HoldingSpecial.Add(other.P2HoldingSpecial);
        NumActions = other.NumActions;
    }

    public void MergeFrom(CodedInputStream input)
    {
        uint tag;
        while ((tag = input.ReadTag()) != 0)
        {
            switch (tag)
            {
                case 10: case 8:  PrevP1Actions.AddEntriesFrom(input, FieldCodec.ForInt64(10)); break;
                case 18: case 16: PrevP2Actions.AddEntriesFrom(input, FieldCodec.ForInt64(18)); break;
                case 26: case 24: P1HoldingSpecial.AddEntriesFrom(input, FieldCodec.ForBool(26)); break;
                case 34: case 32: P2HoldingSpecial.AddEntriesFrom(input, FieldCodec.ForBool(34)); break;
                case 40: NumActions = input.ReadInt64(); break;
                default: input.SkipLastField(); break;
            }
        }
    }

    public void WriteTo(CodedOutputStream output)
    {
        PrevP1Actions.WriteTo(output, FieldCodec.ForInt64(10));
        PrevP2Actions.WriteTo(output, FieldCodec.ForInt64(18));
        P1HoldingSpecial.WriteTo(output, FieldCodec.ForBool(26));
        P2HoldingSpecial.WriteTo(output, FieldCodec.ForBool(34));
        if (NumActions != 0) { output.WriteTag(5, WireFormat.WireType.Varint); output.WriteInt64(NumActions); }
    }

    public int CalculateSize()
    {
        int size = 0;
        size += PrevP1Actions.CalculateSize(FieldCodec.ForInt64(10));
        size += PrevP2Actions.CalculateSize(FieldCodec.ForInt64(18));
        size += P1HoldingSpecial.CalculateSize(FieldCodec.ForBool(26));
        size += P2HoldingSpecial.CalculateSize(FieldCodec.ForBool(34));
        if (NumActions != 0) size += 1 + CodedOutputStream.ComputeInt64Size(NumActions);
        return size;
    }

    public GetBatchEncodedStateInput Clone()
    {
        var clone = new GetBatchEncodedStateInput { NumActions = NumActions };
        clone.PrevP1Actions.Add(PrevP1Actions); clone.PrevP2Actions.Add(PrevP2Actions);
        clone.P1HoldingSpecial.Add(P1HoldingSpecial); clone.P2HoldingSpecial.Add(P2HoldingSpecial);
        return clone;
    }

    public bool Equals(GetBatchEncodedStateInput other) => other != null;
    public override bool Equals(object obj) => Equals(obj as GetBatchEncodedStateInput);
    public override int GetHashCode() => PrevP1Actions.GetHashCode();
    public override string ToString() => $"GetBatchEncodedStateInput {{ envs={PrevP1Actions.Count} }}";
}
