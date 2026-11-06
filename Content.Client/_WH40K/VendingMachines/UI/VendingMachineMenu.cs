using Content.Client._WH40K.VendingMachines.UI;
using Content.Shared._NF.Bank;
using Content.Shared._WH40K.VendingMachines;
using Content.Shared.VendingMachines;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Client.VendingMachines.UI;

public sealed partial class VendingMachineMenu
{
    [Dependency] private IRobustRandom _random = default!;

    public event Func<int, bool>? OnCodeEntered;
    public event Action<VendingMachineKeypadSound, float>? OnAudioPlayed;

    public EntityUid? VendingMachineOwner;
    public EntityUid? User;

    private readonly List<VendingMachineGridSlot> _slots = new();
    private readonly List<(string Id, InventoryType Type)> _entries = new();
    private readonly int[] _bluesScale = [0, 2, 3, 4, 5, 6, 7, 9, 10];
    private readonly string[] _successPhrases = ["wh40k-vending-enjoy", "wh40k-vending-thanks", "wh40k-vending-yum", "wh40k-vending-dispensed"];
    private int? _bufferRow;
    private int? _bufferNumber;
    private long _available;
    private bool _enabled;
    private bool _cursorVisible = true;
    private bool _showingFeedback;
    private int? _pendingIndex;
    private float _blinkTimer;
    private float _inactivityTimer;
    private float _feedbackTimer;
    private float _powerSagTimer;
    private float _chuteRattleDelay;
    private float _chuteRattleTimer;

    public VendingMachineMenu()
    {
        RobustXamlLoader.Load(this);
        IoCManager.InjectDependencies(this);
        BuildNumpad();
        SearchBar.OnTextChanged += _ => ApplyFilter();
        ResetLedIndicators();
        UpdateBufferLabel();
    }

    private void BuildNumpad()
    {
        for (var i = 1; i <= 9; i++)
        {
            var number = i;
            var pitch = MathF.Pow(2f, _bluesScale[number - 1] / 12f);
            var key = new VendingMachineKeypadKey(number.ToString());
            key.OnKeyPressed += () => OnNumberPressed(number, pitch);
            NumpadGrid.AddChild(key);
        }

        var clear = new VendingMachineKeypadKey(Loc.GetString("wh40k-vending-clear"));
        clear.SetTint(Color.FromHex("#c4a6a2"), Color.FromHex("#dfc6c2"), Color.FromHex("#572f2c"));
        clear.OnKeyPressed += () =>
        {
            if (!_enabled || _showingFeedback)
                return;
            PlaySound(VendingMachineKeypadSound.Beep, 0.7f);
            ClearBuffer();
        };
        NumpadGrid.AddChild(clear);

        var zero = new VendingMachineKeypadKey("0");
        zero.OnKeyPressed += () => OnNumberPressed(0, 2f);
        NumpadGrid.AddChild(zero);

        var enter = new VendingMachineKeypadKey(Loc.GetString("wh40k-vending-enter"));
        enter.SetTint(Color.FromHex("#abbda1"), Color.FromHex("#cbd9c2"), Color.FromHex("#34462e"));
        enter.OnKeyPressed += SubmitBuffer;
        NumpadGrid.AddChild(enter);
    }

    public void Populate(List<VendingMachineInventoryEntry> inventory, float priceModifier, int balance,
        int? cashSlotBalance, bool requiresCash, bool enabled = true, bool hasBankAccount = true,
        IReadOnlyDictionary<string, int>? serverPrices = null)
    {
        _enabled = enabled;
        _available = (long) balance + (cashSlotBalance ?? 0);
        BalanceLabel.Text = hasBankAccount
            ? BankSystemExtensions.ToSpesoString(balance)
            : Loc.GetString("wh40k-vending-no-account");
        CashSlotControls.Visible = cashSlotBalance != null;
        CashSlotLabel.Text = BankSystemExtensions.ToSpesoString(cashSlotBalance ?? 0);

        var changed = inventory.Count != _entries.Count;
        for (var i = 0; !changed && i < inventory.Count; i++)
            changed = inventory[i].ID != _entries[i].Id || inventory[i].Type != _entries[i].Type;

        if (changed)
        {
            ItemGrid.RemoveAllChildren();
            LetterGrid.RemoveAllChildren();
            _slots.Clear();
            _entries.Clear();
            _showingFeedback = false;
            _pendingIndex = null;

            for (var i = 0; i < inventory.Count; i++)
            {
                var entry = inventory[i];
                var slot = new VendingMachineGridSlot();
                slot.SetItem(entry.ID, GetCode(i), entry.Amount, entry.Amount == 0);
                var index = i;
                slot.OnItemPressed += () => OnSlotPressed(index);
                ItemGrid.AddChild(slot);
                _slots.Add(slot);
                _entries.Add((entry.ID, entry.Type));
            }

            var rowCount = (inventory.Count + 9) / 10;
            LetterScroll.MinHeight = Math.Min(102f, Math.Max(0, (rowCount + 2) / 3 * 54 - 6));
            for (var i = 0; i < rowCount; i++)
            {
                var row = i;
                var pitch = MathF.Pow(2f, (_bluesScale[row % _bluesScale.Length] - 12) / 12f);
                var key = new VendingMachineKeypadKey(GetRowLabel(row));
                key.OnKeyPressed += () => OnLetterPressed(row, pitch);
                LetterGrid.AddChild(key);
            }
            ClearBuffer();
        }

        for (var i = 0; i < inventory.Count; i++)
        {
            var entry = inventory[i];
            _prototypeManager.TryIndex(entry.ID, out var prototype);
            _slots[i].SetAmount(entry.Amount, entry.Amount == 0);
            var price = serverPrices != null && serverPrices.TryGetValue(entry.ID, out var actual)
                ? actual : GetPrice(entry, prototype, priceModifier);
            _slots[i].SetPrice(requiresCash ? price : 0, _available);
        }

        ApplyFilter();
        if (!_showingFeedback)
            UpdateBufferLabel();
    }

    private void ApplyFilter()
    {
        var filter = SearchBar.Text.Trim();
        for (var i = 0; i < _slots.Count; i++)
            _slots[i].Visible = filter.Length == 0 || _slots[i].ItemName.Contains(filter, StringComparison.CurrentCultureIgnoreCase) ||
                                GetCode(i).Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private void OnLetterPressed(int row, float pitch)
    {
        if (!_enabled || _showingFeedback)
            return;
        _bufferRow = row;
        InputChanged(pitch);
    }

    private void OnNumberPressed(int number, float pitch)
    {
        if (!_enabled || _showingFeedback)
            return;
        _bufferNumber = number;
        InputChanged(pitch);
    }

    private void InputChanged(float pitch)
    {
        PlaySound(VendingMachineKeypadSound.Beep, pitch);
        _cursorVisible = true;
        _blinkTimer = _inactivityTimer = 0f;
        UpdateBufferLabel();
    }

    private void ClearBuffer()
    {
        _bufferRow = _bufferNumber = null;
        _cursorVisible = true;
        _blinkTimer = _inactivityTimer = 0f;
        ResetLedIndicators();
        UpdateBufferLabel();
    }

    private void OnSlotPressed(int index)
    {
        if (!_enabled || _showingFeedback)
            return;

        _bufferRow = index / 10;
        _bufferNumber = index % 10;
        UpdateBufferLabel();
        SubmitBuffer();
    }

    private void SubmitBuffer()
    {
        if (!_enabled || _showingFeedback)
            return;

        if (_bufferRow is not { } row || _bufferNumber is not { } number)
        {
            ShowFeedback(false, "wh40k-vending-invalid");
            return;
        }

        var index = row * 10 + number;
        if (index >= _slots.Count || _slots[index].SoldOut)
        {
            ShowFeedback(false, "wh40k-vending-empty");
            return;
        }
        _pendingIndex = index;
        _showingFeedback = true;
        _feedbackTimer = 5f;
        CodeDisplay.Visible = false;
        BufferLabel.Visible = true;
        BufferLabel.Text = Loc.GetString("wh40k-vending-wait");
        if (OnCodeEntered?.Invoke(index) != true)
            ShowFeedback(false, "wh40k-vending-denied");
    }

    public void CompleteVend(InventoryType type, string id, bool success)
    {
        if (_pendingIndex is not { } index || index >= _entries.Count ||
            _entries[index].Type != type || _entries[index].Id != id)
            return;

        if (success)
        {
            _slots[index].PlayVendAnimation();
            _chuteRattleDelay = 0.55f;
            _chuteRattleTimer = 0.35f;
        }
        var failure = _slots[index].Price > _available ? "wh40k-vending-no-funds" : "wh40k-vending-denied";
        ShowFeedback(success, success ? _random.Pick(_successPhrases) : failure);
    }

    private void ShowFeedback(bool success, string text)
    {
        _pendingIndex = null;
        _bufferRow = _bufferNumber = null;
        _showingFeedback = true;
        _feedbackTimer = 0.75f;
        CodeDisplay.Visible = false;
        BufferLabel.Visible = true;
        BufferLabel.Text = Loc.GetString(text);
        BufferLabel.FontColorOverride = Color.FromHex(success ? "#b0c59f" : "#d69e97");
        if (success)
        {
            GreenLed.Modulate = Color.FromHex("#b1dc96");
            _powerSagTimer = 0.15f;
        }
        else
            RedLed.Modulate = Color.FromHex("#e08176");

        PlaySound(success ? VendingMachineKeypadSound.Success : VendingMachineKeypadSound.Error);
        UpdateHighlights();
    }

    private void PlaySound(VendingMachineKeypadSound sound, float pitch = 1f)
    {
        if (VendingMachineOwner is not { } owner)
            return;

        var path = sound switch
        {
            VendingMachineKeypadSound.Beep => "/Audio/Machines/Nuke/general_beep.ogg",
            VendingMachineKeypadSound.Success => "/Audio/Machines/vending_jingle.ogg",
            VendingMachineKeypadSound.Error => "/Audio/Machines/buzz-two.ogg",
            _ => "/Audio/Machines/button.ogg",
        };
        var volume = sound == VendingMachineKeypadSound.Timeout ? -6f : -4f;
        _entityManager.System<SharedAudioSystem>().PlayPredicted(new SoundPathSpecifier(path), owner, User,
            AudioParams.Default.WithVolume(volume).WithPitchScale(pitch));
        OnAudioPlayed?.Invoke(sound, pitch);
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);
        var dt = args.DeltaSeconds;
        _powerSagTimer = Math.Max(0f, _powerSagTimer - dt);
        var brightness = 1f - Math.Clamp(_powerSagTimer / 0.15f, 0f, 1f) * 0.6f;
        ScreenBackground.Modulate = new Color(brightness, brightness, brightness);

        if (_chuteRattleDelay > 0f)
            _chuteRattleDelay -= dt;
        else if (_chuteRattleTimer > 0f)
        {
            _chuteRattleTimer -= dt;
            var offset = _random.NextFloat(-2f, 2f);
            DispenseChute.Margin = new Thickness(0f, offset, 0f, -offset);
        }
        else
            DispenseChute.Margin = new Thickness(0f);

        if (_showingFeedback)
        {
            _feedbackTimer -= dt;
            if (_feedbackTimer <= 0f)
            {
                if (_pendingIndex != null)
                    ShowFeedback(false, "wh40k-vending-denied");
                else
                {
                    _showingFeedback = false;
                    ClearBuffer();
                }
            }
            return;
        }

        _blinkTimer += dt;
        if (_blinkTimer >= 0.5f)
        {
            _blinkTimer = 0f;
            _cursorVisible = !_cursorVisible;
            UpdateBufferLabel();
        }
        if (_bufferRow != null || _bufferNumber != null)
        {
            _inactivityTimer += dt;
            if (_inactivityTimer >= 15f)
            {
                PlaySound(VendingMachineKeypadSound.Timeout, 0.6f);
                ClearBuffer();
            }
        }
    }

    private void ResetLedIndicators()
    {
        RedLed.Modulate = Color.FromHex("#432a28");
        GreenLed.Modulate = Color.FromHex("#2b3c29");
        BufferLabel.FontColorOverride = Color.FromHex("#b0c59f");
    }

    private void UpdateBufferLabel()
    {
        CodeDisplay.Visible = _enabled;
        BufferLabel.Visible = !_enabled;
        BufferLabel.Text = _enabled ? string.Empty : Loc.GetString("wh40k-vending-offline");
        RowCodeLabel.Text = _bufferRow is { } row ? GetRowLabel(row) : string.Empty;
        NumberCodeLabel.Text = _bufferNumber?.ToString() ?? string.Empty;
        RowPlaceholder.Visible = _bufferRow == null;
        NumberPlaceholder.Visible = _bufferNumber == null;
        RowPlaceholder.Modulate = _cursorVisible ? Color.White : Color.Transparent;
        NumberPlaceholder.Modulate = _bufferRow != null && !_cursorVisible ? Color.Transparent : Color.White;
        InstructionLabel.Text = Loc.GetString(_slots.Count == 0 ? "wh40k-vending-empty" : "wh40k-vending-instructions");
        InstructionLabel.Visible = true;
        SelectionDetails.Visible = false;
        SelectedPriceLabel.Text = string.Empty;
        if (_bufferRow is { } selectedRow && _bufferNumber is { } col && selectedRow * 10 + col < _slots.Count)
        {
            var slot = _slots[selectedRow * 10 + col];
            InstructionLabel.Visible = false;
            SelectionDetails.Visible = true;
            SelectionLabel.Text = FormatSelectionName(slot.ItemName);
            SelectionLabel.ToolTip = slot.ToolTip;
            SelectedPriceLabel.Text = Loc.GetString("wh40k-vending-price", ("price", BankSystemExtensions.ToSpesoString(slot.Price)));
            SelectedPriceLabel.FontColorOverride = Color.FromHex(slot.Price > _available ? "#d3b08b" : "#b9b9b2");
        }
        UpdateHighlights();
    }

    private static string FormatSelectionName(string text)
    {
        return text.Length > 24 ? text[..24] + "..." : text;
    }

    private void UpdateHighlights()
    {
        for (var i = 0; i < _slots.Count; i++)
            _slots[i].SetHighlight(_bufferRow == i / 10, _bufferRow == i / 10 && _bufferNumber == i % 10);
    }

    private static string GetCode(int index) => $"{GetRowLabel(index / 10)}{index % 10}";

    private static string GetRowLabel(int row)
    {
        var label = string.Empty;
        do
        {
            label = string.Concat(((char) ('A' + row % 26)).ToString(), label);
            row = row / 26 - 1;
        } while (row >= 0);
        return label;
    }
}
