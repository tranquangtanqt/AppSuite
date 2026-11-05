using MeRon.Core.Models;
using MeRon.Core.Orders;

namespace MeRon.Tests;

/// <summary>Nhập đơn hàng loạt: đọc chữ dán vào (chép từ sổ ghi tay / Zalo) + ghép khách, món, chữ viết tắt.</summary>
public class BatchOrderTests
{
    private static readonly DateOnly Today = new(2026, 11, 3);

    private static readonly Customer[] Customers =
    [
        new(1, "Mỹ Ly", "706", null, null),
        new(2, "Tuấn Chung", "W1", null, null),
        new(3, "Thu Hà", "205", null, null),
        new(4, "Hà W3", "W3", null, null), // MeRon không cho trùng tên khách: khách cùng tên thường kèm toà
        new(5, "Hà W1", "W1", null, null),
        new(6, "Thảo", "1101", null, null),
    ];

    private static readonly Product[] Products =
    [
        new(10, 1, "Sữa chua uống", null, 15000, null),
        new(11, 1, "Sữa chua dẻo", null, 12000, null),
        new(12, 1, "Bún chả", null, 40000, null),
        new(13, 1, "Bún chả cá", null, 45000, null),
        new(14, 1, "Phô mai mận", null, 20000, null),
        new(15, 1, "Giò", null, 30000, null),
        new(16, 1, "Nấm", null, 5000, null),
    ];

    private static readonly Abbreviations Abbr = new(Abbreviations.DefaultText);

    private static BatchLineMatch MatchLine(string text) =>
        BatchOrderMatcher.Match(BatchOrderParser.Parse(text, Today).Lines.Single(), Customers, Products, Abbr);

    [Fact]
    public void Parses_claude_format_with_day_price_and_ck()
    {
        var text = BatchOrderParser.Parse("Ngày | 29/10\n\n706 Mỹ Ly | 3 sc uống\n1201 | 1 phần cá | 45k | CK\n", Today);

        Assert.Equal(new DateOnly(2026, 10, 29), text.Day);
        Assert.Equal(2, text.Lines.Count);
        Assert.Equal(3, text.Lines[0].Number);
        Assert.Equal(new BatchItem(3, "sc uống", null, false, "3 sc uống"), text.Lines[0].Items.Single());
        Assert.Equal(45000, text.Lines[1].Price);
        Assert.True(text.Lines[1].Paid);
        Assert.False(text.Lines[0].Paid);
    }

    [Fact]
    public void Parses_notebook_style_items_glued_quantity_plus_and_trailing_ck()
    {
        var line = BatchOrderParser.Parse("W1 Tuấn Chung: 1bún + 10 nấm + giò x2 + 1 phần cá 45k CK", Today).Lines.Single();

        Assert.Equal("W1 Tuấn Chung", line.Customer);
        Assert.True(line.Paid);
        Assert.Equal([(1L, "bún"), (10L, "nấm"), (2L, "giò"), (1L, "phần cá")], line.Items.Select(i => (i.Quantity, i.Name)).ToArray());
        Assert.Equal(45000, line.Items[3].Price);
    }

    [Theory]
    [InlineData("45k", 45000L)]
    [InlineData("1,5k", 1500L)]
    [InlineData("45.000", 45000L)]
    [InlineData("45000", 45000L)]
    [InlineData("abc", null)]
    [InlineData("0", null)]
    public void Parses_prices(string text, long? expected) => Assert.Equal(expected, BatchOrderParser.ParsePrice(text));

    [Fact]
    public void Line_without_separator_is_reported()
    {
        var match = MatchLine("706 Mỹ Ly 3 sc uống");
        Assert.False(match.CanSave);
        Assert.Contains("khách | món", match.Problems.Single());
    }

    [Fact]
    public void Matches_customer_by_apartment_and_name_and_expands_abbreviations()
    {
        var match = MatchLine("706 Mỹ Ly | 3 sc uống; 4 pm mận");

        Assert.True(match.CanSave);
        Assert.Equal(1, match.Customer!.Id);
        Assert.Equal([10L, 14L], match.Items.Select(i => i.Product!.Id).ToArray());
        Assert.Equal(3 * 15000 + 4 * 20000, match.Total);
    }

    [Fact]
    public void Apartment_number_must_match_whole_word()
    {
        Assert.Equal(6, MatchLine("1101 | 1 giò").Customer!.Id);
        Assert.Contains("Chưa có khách \"101\"", MatchLine("101 | 1 giò").Problems);
    }

    [Fact]
    public void Several_customers_with_the_same_name_must_be_chosen_unless_apartment_given()
    {
        var ambiguous = MatchLine("Hà | 1 giò");
        Assert.Null(ambiguous.Customer);
        Assert.Equal(3, ambiguous.CustomerCandidates.Count); // Thu Hà, Hà (W3), Hà (W1)
        Assert.Contains("khách khớp", ambiguous.Problems.Single());

        Assert.Equal(5, MatchLine("W1 Hà | 1 giò").Customer!.Id);
    }

    /// <summary>Tên khách thường kèm số phòng / toà ("w3 Ngọc"): chọn trong ô Chọn khách phải ghép được đúng khách đó.</summary>
    [Fact]
    public void Choosing_a_customer_whose_name_contains_digits_resolves_the_line()
    {
        Customer[] customers =
        [
            new(1, "w3 Ngọc", "w3", null, null),
            new(2, "w3 C Ngọc Thủy", "w3", null, null),
            new(3, "Ngọc Nam w3", "w3", null, null),
        ];
        BatchLineMatch Match(string text, params Customer[] extra) =>
            BatchOrderMatcher.Match(BatchOrderParser.Parse(text, Today).Lines.Single(), [.. customers, .. extra], Products, Abbr);

        var ambiguous = Match("Ngọc w3 | 1 giò");
        Assert.Null(ambiguous.Customer);
        Assert.True(ambiguous.CustomerCandidates.Count > 1);

        foreach (var chosen in customers.Where(c => ambiguous.CustomerCandidates.Contains(c)))
        {
            var rewritten = BatchOrderParser.Format(ambiguous.Line, customer: chosen.Name);
            Assert.Equal(chosen.Id, Match(rewritten).Customer?.Id);
        }

        Assert.Equal(1, Match("w3 Ngọc | 1 giò").Customer?.Id); // gõ đúng nguyên tên: khỏi phải chọn

        // Khách cùng toà tên ngắn hơn: không nhận nhầm sang nhau
        Customer shortName = new(4, "Ngọc", "w3", null, null);
        Assert.Equal(1, Match("w3 Ngọc | 1 giò", shortName).Customer?.Id);
        Assert.Equal(4, Match("Ngọc | 1 giò", shortName).Customer?.Id);
    }

    /// <summary>Nút Chọn khách: chữ không khớp ai, thay bằng khách đã có; món, giá, CK trên dòng giữ nguyên.</summary>
    [Fact]
    public void Replacing_an_unknown_customer_keeps_the_rest_of_the_line()
    {
        var unknown = MatchLine("w3 Ngc? | 2 giò; 1 sc uống | 45k | CK");
        Assert.Empty(unknown.CustomerCandidates);
        Assert.Contains("Chưa có khách", unknown.Problems.Single());

        var rewritten = BatchOrderParser.Format(unknown.Line, customer: "Mỹ Ly");
        Assert.Equal("Mỹ Ly | 2 giò; 1 sc uống | 45k | CK", rewritten);

        var fixedLine = MatchLine(rewritten);
        Assert.Equal(1, fixedLine.Customer?.Id);
        Assert.False(fixedLine.Line.CustomerUnsure);
        Assert.True(fixedLine.Line.Paid);
        Assert.Equal(45000, fixedLine.Line.Price);
    }

    [Fact]
    public void Product_name_ignores_spaces_and_accents_but_must_be_unique()
    {
        Assert.Equal(14, MatchLine("706 | 1 phomai mận").Items.Single().Product!.Id);
        var bun = MatchLine("706 | 1 bún").Items.Single();
        Assert.Null(bun.Product);
        Assert.Equal(2, bun.Candidates.Count); // Bún chả, Bún chả cá
        Assert.Equal(13, MatchLine("706 | 1 bún chả cá").Items.Single().Product!.Id);
    }

    [Fact]
    public void Unknown_item_with_price_is_a_free_item_and_line_price_goes_to_the_only_unknown_item()
    {
        var inline = MatchLine("706 | 1 phần cá 45k");
        Assert.True(inline.CanSave);
        Assert.Null(inline.Items.Single().Product);
        Assert.Equal(45000, inline.Items.Single().FreePrice);

        // Người dùng chốt: món tự do tên = chữ như ghi kể cả số lượng, số lượng 1, đơn giá = giá ghi ("5 lạng tôm 100k" = 100.000).
        var shrimp = MatchLine("1101 | 5 lạng tôm 100k").Items.Single();
        Assert.Equal(("5 lạng tôm", 1L, 100000L), (shrimp.FreeName, shrimp.Quantity, shrimp.UnitPrice));

        var column = MatchLine("706 | 1 giò; 2 phần cá | 45k | CK");
        Assert.True(column.CanSave);
        Assert.Equal(30000 + 45000, column.Total);

        var draft = column.ToDraft(new DateOnly(2026, 10, 29));
        Assert.True(draft.IsPrepaid);
        Assert.Equal(new DateTime(2026, 10, 29), draft.Delivery);
        Assert.Equal(new OrderLineDraft(null, 1, true, "2 phần cá", 45000), draft.Lines[1]);
        OrderCalculator.Validate(draft); // lưu được như đơn nhập tay
    }

    [Fact]
    public void Unknown_item_without_price_blocks_saving()
    {
        var match = MatchLine("706 | 5 pama");
        Assert.False(match.CanSave);
        Assert.Contains("Món lạ \"pama\"", match.Problems.Single());
    }

    [Fact]
    public void Question_mark_is_a_warning_not_a_problem()
    {
        var match = MatchLine("706 Mỹ Ly? | 3 sc uống?");
        Assert.True(match.CanSave);
        Assert.Equal(2, match.Warnings.Count);
        Assert.Equal("Mỹ Ly", match.Customer!.Name);
    }

    [Fact]
    public void Format_rewrites_chosen_customer_and_item_and_replace_remove_lines()
    {
        var line = BatchOrderParser.Parse("Hà? | 1 bún; 2 phần cá? | 45k | CK", Today).Lines.Single();
        Assert.Equal("Hà W1 | 1 Bún chả cá; 2 phần cá? | 45k | CK", BatchOrderParser.Format(line, customer: "Hà W1", itemIndex: 0, itemName: "Bún chả cá"));

        Assert.Equal("a\nX\nc", BatchOrderParser.ReplaceLine("a\r\nb\r\nc", 2, "X"));
        Assert.Equal("a\nX\nc", BatchOrderParser.ReplaceLine("a\rb\rc", 2, "X")); // ô nhập WinUI xuống dòng bằng \r
        Assert.Equal([1, 2], BatchOrderParser.Parse("706 | 1 giò\r205 | 1 giò", Today).Lines.Select(l => l.Number).ToArray());
        Assert.Equal("Ngày | 29/10\nc", BatchOrderParser.RemoveLines("Ngày | 29/10\nb\nc", [2]));
    }

    [Fact]
    public void Abbreviations_replace_whole_words_only()
    {
        var abbr = new Abbreviations("SC = sữa chua\n bad line \nbb=bánh bao");
        Assert.Equal(2, abbr.Count);
        Assert.Equal("sữa chua uống", abbr.Expand("sc uống"));
        Assert.Equal("bánh bao scx", abbr.Expand("bb scx"));
    }

    // ---------- So khớp với đơn đã có trong MeRon cùng ngày ----------

    private static Order DbOrder(long id, long userId, OrderStatus status, params (long? ProductId, string Name, long Price, long Quantity)[] items) =>
        new(id, userId, null, status, "2026-10-29", null, 0, 0, 0, null,
            items.Select((x, i) => new OrderItem(i, id, x.ProductId, x.Name, x.Price, x.Quantity, x.Price * x.Quantity)).ToList());

    private static IReadOnlyList<ReconcileEntry> Reconcile(string text, params Order[] orders) =>
        BatchReconciler.Compare(BatchOrderParser.Parse(text, Today).Lines.Select(l => BatchOrderMatcher.Match(l, Customers, Products, Abbr)).ToList(), orders);

    [Fact]
    public void Reconcile_sorts_lines_and_orders_into_matched_different_only_in_book_and_only_in_app()
    {
        var entries = Reconcile(
            "706 Mỹ Ly | 3 sc uống\n" +                 // khớp (đơn 1)
            "W1 Tuấn Chung | 2 giò; 1 nấm | | CK\n" +   // lệch với đơn 2
            "205 Thu Hà | 1 giò\n" +                    // chưa có trong MeRon
            "999 | 1 giò",                              // chưa ghép khách
            DbOrder(1, 1, OrderStatus.Shipping, (10, "Sữa chua uống", 15000, 3)),
            DbOrder(2, 2, OrderStatus.Shipping, (15, "Giò", 30000, 1), (12, "Bún chả", 40000, 1)),
            DbOrder(3, 6, OrderStatus.Completed, (15, "Giò", 30000, 1)));  // Thảo: không có trong sổ

        Assert.Equal(ReconcileKind.NotComparable, entries.Single(e => e.Draft?.Line.Customer == "999").Kind);
        Assert.Equal(1, entries.Single(e => e.Kind == ReconcileKind.Matched).Order!.Id);
        Assert.Equal("Thu Hà", entries.Single(e => e.Kind == ReconcileKind.OnlyInDraft).Draft!.Customer!.Name);
        Assert.Equal(3, entries.Single(e => e.Kind == ReconcileKind.OnlyInDatabase).Order!.Id);

        var different = entries.Single(e => e.Kind == ReconcileKind.Different);
        Assert.Equal(2, different.Order!.Id);
        Assert.Equal(
            ["Giò: sổ 2, MeRon 1", "MeRon thiếu 1 Nấm", "MeRon thừa 1 Bún chả", "sổ ghi CK, MeRon chưa thanh toán"],
            different.Differences);
    }

    [Fact]
    public void Reconcile_pairs_identical_orders_first_when_a_customer_has_two_orders()
    {
        var entries = Reconcile(
            "706 Mỹ Ly | 1 giò\n706 Mỹ Ly | 3 sc uống",
            DbOrder(1, 1, OrderStatus.Shipping, (10, "Sữa chua uống", 15000, 3)),
            DbOrder(2, 1, OrderStatus.Shipping, (15, "Giò", 30000, 1)));

        Assert.All(entries, e => Assert.Equal(ReconcileKind.Matched, e.Kind));
        Assert.Equal(2, entries.Single(e => e.Draft!.Line.Number == 1).Order!.Id);
    }

    [Fact]
    public void Reconcile_compares_free_items_by_name_without_accents_and_price()
    {
        var same = Reconcile("706 | 1 phần cá 45k", DbOrder(1, 1, OrderStatus.Shipping, (null, "1 Phần cá", 45000, 1)));
        Assert.Equal(ReconcileKind.Matched, same.Single().Kind);

        var otherPrice = Reconcile("706 | 1 phần cá 45k", DbOrder(1, 1, OrderStatus.Shipping, (null, "1 phần cá", 50000, 1)));
        Assert.Equal(ReconcileKind.Different, otherPrice.Single().Kind);
    }
}
