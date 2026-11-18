using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;
using Xunit.Abstractions;

namespace OceanBase.EntityFrameworkCore.LiveTests
{
    public sealed class LiveStringQueryTests(ITestOutputHelper output)
    {
        public static IEnumerable<object[]> Cases()
        {
            foreach (var mode in Enum.GetValues<OceanBaseCompatibilityMode>())
            foreach (var async in new[] { false, true })
            foreach (var nulls in new[] { false, true })
            {
                yield return [mode, async, nulls];
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task Captured_and_literal_searches_keep_text_metacharacters_null_guards_and_current_values(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => await WithTable(mode, async, nulls, async (context, seed, stage) =>
            {
                string? needle = "first";
                var rows = context.Set<Row>();
                var query = rows.OrderBy(row => row.Id).Select(row => new
                {
                    row.Id, Contains = row.Plain!.Contains(needle!), Starts = row.Plain!.StartsWith(needle!),
                    Ends = row.Plain!.EndsWith(needle!), Negated = !row.Plain!.Contains(needle!)
                });
                var filtered = rows.Where(row => row.Plain!.Contains(needle!)).OrderBy(row => row.Id).Select(row => row.Id);
                foreach (var value in new string?[] { "%_\\'", "海🌊", "AbC", "AbC ", " ", "", null, "missing", "", "%_\\'" })
                {
                    needle = value;
                    stage("captured search value class=" + (value is null ? "null" : value.Length == 0 ? "empty" : "text"));
                    Assert.Equal(seed.Select(row => new
                    {
                        row.Id, Contains = Match(row.Plain, needle, 0, context), Starts = Match(row.Plain, needle, 1, context),
                        Ends = Match(row.Plain, needle, 2, context), Negated = !Match(row.Plain, needle, 0, context)
                    }), await Read(query, async));
                    Assert.Equal(seed.Where(row => Match(row.Plain, needle, 0, context)).Select(row => row.Id), await Read(filtered, async));
                }
                stage("known literal empty/null needles and literal metacharacters");
                var literal = rows.OrderBy(row => row.Id).Select(row => new
                {
                    row.Id, Empty = row.Plain!.Contains("") && row.Plain.StartsWith("") && row.Plain.EndsWith(""),
                    Null = row.Plain!.Contains((string)null!), Wildcard = row.Plain!.Contains("%_\\'")
                });
                Assert.Equal(seed.Select(row => new
                {
                    row.Id, Empty = row.Plain is not null, Null = false, Wildcard = Match(row.Plain, "%_\\'", 0, context)
                }), await Read(literal, async));
                Assert.Empty(context.ChangeTracker.Entries());
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task Column_and_computed_needles_keep_total_booleans_in_both_null_modes(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => await WithTable(mode, async, nulls, async (context, seed, stage) =>
            {
                stage("column needles and computed empty/null needles");
                var rows = context.Set<Row>();
                var query = rows.OrderBy(row => row.Id).Select(row => new
                {
                    row.Id, Contains = row.Plain!.Contains(row.Needle!), Starts = row.Plain!.StartsWith(row.Needle!),
                    Ends = row.Plain!.EndsWith(row.Needle!), Negated = !row.Plain!.EndsWith(row.Needle!),
                    Computed = row.Plain!.Contains(row.Required.Substring(0, 0))
                });
                Assert.Equal(seed.Select(row => new
                {
                    row.Id, Contains = Match(row.Plain, row.Needle, 0, context), Starts = Match(row.Plain, row.Needle, 1, context),
                    Ends = Match(row.Plain, row.Needle, 2, context), Negated = !Match(row.Plain, row.Needle, 2, context),
                    Computed = mode == OceanBaseCompatibilityMode.MySql && row.Plain is not null
                }), await Read(query, async));
                Assert.Equal(seed.Where(row => !Match(row.Plain, row.Needle, 2, context)).Select(row => row.Id),
                    await Read(rows.Where(row => !row.Plain!.EndsWith(row.Needle!)).OrderBy(row => row.Id).Select(row => row.Id), async));
                var nested = rows.OrderBy(row => row.Id).Select(row => new
                {
                    row.Id, Value = (row.Id % 2 == 0 ? row.Plain : row.Required)!.ToUpper().Contains(row.Needle!.ToUpper())
                });
                Assert.Equal(seed.Select(row => new
                {
                    row.Id, Value = Match((row.Id % 2 == 0 ? row.Plain : row.Required)?.ToUpperInvariant(), row.Needle?.ToUpperInvariant(), 0, context)
                }), await Read(nested, async));
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task Character_searches_preserve_text_parameters_columns_and_native_comparisons(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => await WithTable(mode, async, nulls, async (context, seed, stage) =>
            {
                var rows = context.Set<Row>();
                var needle = '海';
                var query = rows.OrderBy(row => row.Id).Select(row => new
                {
                    row.Id, Contains = row.Plain!.Contains(needle), Starts = row.Plain!.StartsWith(needle),
                    Ends = row.Plain!.EndsWith(needle), Negated = !row.Plain!.Contains(needle),
                    National = row.National!.Contains(needle), Fixed = row.Fixed!.EndsWith(needle),
                    NationalFixed = row.NationalFixed!.StartsWith(needle), Lob = row.Large!.EndsWith(needle),
                    Converted = row.Converted!.EndsWith(needle)
                });
                var filtered = rows.Where(row => !row.Plain!.EndsWith(needle)).OrderBy(row => row.Id).Select(row => row.Id);
                foreach (var value in new[] { '海', '%', '_', '\\', '\'', 'A', ' ', 'd', '海' })
                {
                    needle = value;
                    stage("char scalar parameter refresh and native text families");
                    var text = needle.ToString();
                    Assert.Equal(seed.Select(row => new
                    {
                        row.Id, Contains = Match(row.Plain, text, 0, context), Starts = Match(row.Plain, text, 1, context),
                        Ends = Match(row.Plain, text, 2, context), Negated = !Match(row.Plain, text, 0, context),
                        National = Match(row.National, text, 0, context), Fixed = Match(row.Fixed, text, 2, context),
                        NationalFixed = Match(row.NationalFixed, text, 1, context), Lob = Match(row.Large, text, 2, context),
                        Converted = Match(Encode(row.Converted), Encode(text), 2, context)
                    }), await Read(query, async));
                    Assert.Equal(seed.Where(row => !Match(row.Plain, text, 2, context)).Select(row => row.Id), await Read(filtered, async));
                }
                stage("nullable char columns, computed chars and literals");
                var columns = rows.OrderBy(row => row.Id).Select(row => new
                {
                    row.Id, Contains = row.Plain!.Contains(row.Character!.Value), Starts = row.Plain!.StartsWith(row.Character!.Value),
                    Ends = row.Plain!.EndsWith(row.Character!.Value), Negated = !row.Plain!.EndsWith(row.Character!.Value),
                    Converted = row.Converted!.Contains(row.Character!.Value), Lob = row.Large!.EndsWith(row.Character!.Value),
                    Computed = (row.Plain ?? row.Required).ToUpper().Contains(row.Character ?? 'X'),
                    Literal = row.Plain!.Contains('海') || row.Plain!.StartsWith('%') || row.Plain!.EndsWith('_')
                });
                Assert.Equal(seed.Select(row => new
                {
                    row.Id, Contains = Match(row.Plain, row.Character?.ToString(), 0, context), Starts = Match(row.Plain, row.Character?.ToString(), 1, context),
                    Ends = Match(row.Plain, row.Character?.ToString(), 2, context), Negated = !Match(row.Plain, row.Character?.ToString(), 2, context),
                    Converted = Match(Encode(row.Converted), row.Character?.ToString(), 0, context), Lob = Match(row.Large, row.Character?.ToString(), 2, context),
                    Computed = Match((row.Plain ?? row.Required).ToUpperInvariant(), (row.Character ?? 'X').ToString(), 0, context),
                    Literal = Match(row.Plain, "海", 0, context) || Match(row.Plain, "%", 1, context) || Match(row.Plain, "_", 2, context)
                }), await Read(columns, async));
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task Equality_concat_and_coalesce_preserve_native_comparisons_and_empty_normalization(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => await WithTable(mode, async, nulls, async (context, seed, stage) =>
            {
                var rows = context.Set<Row>();
                string? target = "first";
                var equal = rows.Where(row => row.Plain == target).OrderBy(row => row.Id).Select(row => row.Id);
                var equals = rows.Where(row => row.Plain!.Equals(target)).OrderBy(row => row.Id).Select(row => row.Id);
                var staticEquals = rows.Where(row => string.Equals(row.Plain, target)).OrderBy(row => row.Id).Select(row => row.Id);
                var unequal = rows.Where(row => row.Plain != target).OrderBy(row => row.Id).Select(row => row.Id);
                foreach (var value in new string?[] { "AbC", "abc ", "", null, "海🌊海end" })
                {
                    target = value;
                    stage("captured equality/inequality and native empty semantics");
                    var normalized = Normalize(target, mode);
                    var expected = seed.Where(row => EqualText(row.Plain, normalized, context)).Select(row => row.Id).ToArray();
                    Assert.Equal(expected, await Read(equal, async));
                    Assert.Equal(expected, await Read(equals, async));
                    Assert.Equal(expected, await Read(staticEquals, async));
                    Assert.Equal(seed.Where(row => !EqualText(row.Plain, normalized, context)
                        && (!nulls || normalized is null || row.Plain is not null)).Select(row => row.Id), await Read(unequal, async));
                }
                stage("column equality and fixed-arity concat");
                Assert.Equal(seed.Where(row => (!nulls || row.Plain is not null && row.Needle is not null)
                    && EqualText(row.Plain, row.Needle, context)).Select(row => row.Id),
                    await Read(rows.Where(row => row.Plain == row.Needle).OrderBy(row => row.Id).Select(row => row.Id), async));
                var concat = rows.OrderBy(row => row.Id).Select(row => new
                {
                    row.Id, Two = (string?)(row.Plain + row.Other), Three = (string?)string.Concat(row.Plain, "-", row.Other),
                    Four = (string?)string.Concat(row.Plain, "-", row.Other, "!"), Fallback = (row.Plain + row.Other) ?? "fallback"
                });
                Assert.Equal(seed.Select(row => new
                {
                    row.Id, Two = Normalize(row.Plain + row.Other, mode), Three = Normalize(string.Concat(row.Plain, "-", row.Other), mode),
                    Four = Normalize(string.Concat(row.Plain, "-", row.Other, "!"), mode),
                    Fallback = Normalize(row.Plain + row.Other, mode) ?? "fallback"
                }), await Read(concat, async));
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task Unary_functions_keep_space_only_trim_character_length_and_function_null_tests(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => await WithTable(mode, async, nulls, async (context, seed, stage) =>
            {
                stage("case/trim/character length including supplementary and non-space whitespace");
                var rows = context.Set<Row>();
                var query = rows.OrderBy(row => row.Id).Select(row => new
                {
                    row.Id, Lower = (string?)row.Plain!.ToLower(), Upper = (string?)row.Plain!.ToUpper(), Trim = (string?)row.Plain!.Trim(),
                    Start = (string?)row.Plain!.TrimStart(), End = (string?)row.Plain!.TrimEnd(), Length = (int?)row.Plain!.Length,
                    RequiredTrimNull = row.Required.Trim() == null
                });
                Assert.Equal(seed.Select(row => new
                {
                    row.Id, Lower = row.Plain?.ToLowerInvariant(), Upper = row.Plain?.ToUpperInvariant(),
                    Trim = Normalize(row.Plain?.Trim(' '), mode), Start = Normalize(row.Plain?.TrimStart(' '), mode),
                    End = Normalize(row.Plain?.TrimEnd(' '), mode), Length = Characters(row.Plain),
                    RequiredTrimNull = Normalize(row.Required.Trim(' '), mode) is null
                }), await Read(query, async));
                Assert.Equal(seed.Where(row => Normalize(row.Required.Trim(' '), mode) is null).Select(row => row.Id),
                    await Read(rows.Where(row => row.Required.Trim() == null).OrderBy(row => row.Id).Select(row => row.Id), async));
                var trimmedGroups = rows.GroupBy(row => row.Required.Trim()).Select(group => new { Key = (string?)group.Key, Count = group.Count() });
                var groups = await Read(trimmedGroups, async);
                var comparer = context.IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
                var expectedGroups = seed.GroupBy(row => Normalize(row.Required.Trim(' '), mode), comparer)
                    .Select(group => (Key: group.Key, Count: group.Count())).ToArray();
                Assert.Equal(expectedGroups.Length, groups.Length);
                foreach (var group in groups)
                {
                    Assert.Equal(Assert.Single(expectedGroups, expected => comparer.Equals(expected.Key, group.Key)).Count, group.Count);
                }
                var filtered = rows.Where(row => row.Plain!.Trim() != "missing").OrderBy(row => row.Id).Select(row => row.Id);
                Assert.Equal(seed.Where(row => !nulls || Normalize(row.Plain?.Trim(' '), mode) is not null).Select(row => row.Id), await Read(filtered, async));
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task Substring_uses_native_character_bounds_for_captured_constant_and_column_arguments(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => await WithTable(mode, async, nulls, async (context, seed, stage) =>
            {
                var start = 0;
                var length = 1;
                var rows = context.Set<Row>();
                var query = rows.OrderBy(row => row.Id).Select(row => new
                {
                    row.Id, Tail = (string?)row.Plain!.Substring(start), Part = (string?)row.Plain!.Substring(start, length),
                    IsNull = row.Required.Substring(start, length) == null
                });
                foreach (var bounds in new[] { (0, 1), (1, 2), (0, 0), (-1, 2), (-2, 2), (int.MinValue, 1), (int.MaxValue, int.MaxValue), (0, -1), (0, int.MaxValue), (100, 2) })
                {
                    (start, length) = bounds;
                    stage($"substring start={start} length={length}");
                    Assert.Equal(seed.Select(row => new
                    {
                        row.Id, Tail = Substring(row.Plain, start, null, mode), Part = Substring(row.Plain, start, length, mode),
                        IsNull = Substring(row.Required, start, length, mode) is null
                    }), await Read(query, async));
                }
                stage("constant and column substring origin/bounds");
                var constants = rows.OrderBy(row => row.Id).Select(row => new { row.Id, Value = (string?)row.Plain!.Substring(0, 1), Far = (string?)row.Plain!.Substring(int.MaxValue) });
                Assert.Equal(seed.Select(row => new { row.Id, Value = Substring(row.Plain, 0, 1, mode), Far = Substring(row.Plain, int.MaxValue, null, mode) }), await Read(constants, async));
                var columns = rows.OrderBy(row => row.Id).Select(row => new { row.Id, Value = (string?)row.Plain!.Substring(row.Start, row.Length) });
                Assert.Equal(seed.Select(row => new { row.Id, Value = Substring(row.Plain, row.Start, row.Length, mode) }), await Read(columns, async));
                Assert.Equal(8, async ? await rows.CountAsync() : rows.Count());
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task Remove_preserves_native_characters_valid_ranges_and_null_results(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => await WithTable(mode, async, nulls, async (context, seed, stage) =>
            {
                var start = 0;
                var count = 0;
                var rows = context.Set<Row>();
                var query = rows.OrderBy(row => row.Id).Select(row => new
                {
                    row.Id, Tail = (string?)row.Plain!.Remove(start), Range = (string?)row.Plain!.Remove(start, count),
                    National = (string?)row.National!.Remove(start, count), Fixed = (string?)row.Fixed!.Remove(start, count),
                    NationalFixed = (string?)row.NationalFixed!.Remove(start), Lob = (string?)row.Large!.Remove(start, count),
                    LobTail = (string?)row.Large!.Remove(start), IsNull = row.Required.Remove(start, count) == null
                });
                var filtered = rows.Where(row => row.Plain!.Remove(start, count) == null).OrderBy(row => row.Id).Select(row => row.Id);
                foreach (var bounds in new[] { (0, 0), (0, 1), (1, 2), (2, 1), (-1, 1), (int.MaxValue, int.MaxValue),
                    (int.MinValue, int.MinValue), (0, -1), (0, int.MaxValue), (100, 2), (0, 0) })
                {
                    (start, count) = bounds;
                    stage($"remove start={start} count={count}");
                    Assert.Equal(seed.Select(row => new
                    {
                        row.Id, Tail = Remove(row.Plain, start, null, mode, false), Range = Remove(row.Plain, start, count, mode, true),
                        National = Remove(row.National, start, count, mode, true), Fixed = Remove(row.Fixed, start, count, mode, true),
                        NationalFixed = Remove(row.NationalFixed, start, null, mode, false), Lob = Remove(row.Large, start, count, mode, true),
                        LobTail = Remove(row.Large, start, null, mode, false), IsNull = Remove(row.Required, start, count, mode, true) is null
                    }), await Read(query, async));
                    Assert.Equal(seed.Where(row => Remove(row.Plain, start, count, mode, true) is null).Select(row => row.Id), await Read(filtered, async));
                }
                stage("nullable native bounds, full removal, end index and composition");
                var columns = rows.OrderBy(row => row.Id).Select(row => new
                {
                    row.Id, Value = (string?)row.Plain!.Remove(row.RemoveStart!.Value, row.RemoveCount!.Value),
                    Tail = (string?)row.Plain!.Remove(row.RemoveStart!.Value),
                    All = (string?)row.Plain!.Remove(0, row.Plain.Length), End = (string?)row.Plain!.Remove(row.Plain.Length),
                    EmptyLob = (string?)row.Large!.Substring(0, 0).Remove(0),
                    EmptyLobRange = (string?)row.Large!.Substring(0, 0).Remove(0, 0),
                    Composed = row.Plain!.Remove(1, 1).ToUpper().Contains('海')
                });
                Assert.Equal(seed.Select(row => new
                {
                    row.Id, Value = Remove(row.Plain, row.RemoveStart, row.RemoveCount, mode, true),
                    Tail = Remove(row.Plain, row.RemoveStart, null, mode, false),
                    All = Remove(row.Plain, 0, Characters(row.Plain), mode, true), End = Remove(row.Plain, Characters(row.Plain), null, mode, false),
                    EmptyLob = mode == OceanBaseCompatibilityMode.Oracle || row.Large is null ? null : "",
                    EmptyLobRange = mode == OceanBaseCompatibilityMode.Oracle || row.Large is null ? null : "",
                    Composed = Match(Remove(row.Plain, 1, 1, mode, true)?.ToUpperInvariant(), "海", 0, context)
                }), await Read(columns, async));
                stage("stored-text converter result ownership");
                var converted = Assert.Single(await Read(rows.Where(row => row.Id == 1).Select(row => new
                {
                    Tail = (string?)row.Converted!.Remove(2), Range = (string?)row.Converted!.Remove(1, 2)
                }), async));
                Assert.Equal(Decode(Remove(Encode(seed[0].Converted), 2, null, mode, false)), converted.Tail);
                Assert.Equal(Decode(Remove(Encode(seed[0].Converted), 1, 2, mode, true)), converted.Range);
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task Fixed_and_national_functions_keep_padding_characters_and_result_families(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => await WithTable(mode, async, nulls, async (context, seed, stage) =>
            {
                stage("fixed ordinary/national padding and variable national functions");
                var rows = context.Set<Row>();
                var query = rows.OrderBy(row => row.Id).Select(row => new
                {
                    row.Id, PlainLength = (int?)row.Fixed!.Length, NationalLength = (int?)row.NationalFixed!.Length,
                    PlainTrim = (string?)row.Fixed!.Trim(), NationalTrim = (string?)row.NationalFixed!.Trim(),
                    PlainPart = (string?)row.Fixed!.Substring(0, 2), NationalPart = (string?)row.NationalFixed!.Substring(0, 2),
                    Upper = (string?)row.National!.ToUpper(), Lower = (string?)row.National!.ToLower(), Length = (int?)row.National!.Length,
                    Part = (string?)row.National!.Substring(1, 2), Search = row.National!.Contains("海🌊")
                });
                Assert.Equal(seed.Select(row => new
                {
                    row.Id, PlainLength = Characters(row.Fixed), NationalLength = Characters(row.NationalFixed),
                    PlainTrim = Normalize(row.Fixed?.Trim(' '), mode), NationalTrim = Normalize(row.NationalFixed?.Trim(' '), mode),
                    PlainPart = Substring(row.Fixed, 0, 2, mode), NationalPart = Substring(row.NationalFixed, 0, 2, mode),
                    Upper = row.National?.ToUpperInvariant(), Lower = row.National?.ToLowerInvariant(), Length = Characters(row.National),
                    Part = Substring(row.National, 1, 2, mode), Search = Match(row.National, "海🌊", 0, context)
                }), await Read(query, async));
                var ordered = rows.Where(row => row.Fixed!.Trim() == "abc").OrderBy(row => row.Id).Select(row => row.Id);
                Assert.Equal(seed.Where(row => EqualText(Normalize(row.Fixed?.Trim(' '), mode), "abc", context)).Select(row => row.Id), await Read(ordered, async));
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task Converter_results_apply_inverse_once_and_unsupported_predicates_reject_before_execution(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => await WithTable(mode, async, nulls, async (context, seed, stage) =>
            {
                stage("stored-value converter functions and current converted needles");
                var rows = context.Set<Row>();
                var query = rows.OrderBy(row => row.Id).Select(row => new
                {
                    row.Id, Upper = (string?)row.Converted!.ToUpper(), Lower = (string?)row.Converted!.ToLower(),
                    Trim = (string?)row.Converted!.Trim(), Part = (string?)row.Converted!.Substring(0, 2), Length = (int?)row.Converted!.Length
                });
                Assert.Equal(seed.Select(row => new
                {
                    row.Id, Upper = Decode(Encode(row.Converted)?.ToUpperInvariant()), Lower = Decode(Encode(row.Converted)?.ToLowerInvariant()),
                    Trim = Decode(Normalize(Encode(row.Converted)?.Trim(' '), mode)), Part = Decode(Substring(Encode(row.Converted), 0, 2, mode)),
                    Length = Characters(Encode(row.Converted))
                }), await Read(query, async));
                string? needle = "first";
                var converted = rows.OrderBy(row => row.Id).Select(row => new { row.Id, Match = row.Converted!.Contains(needle!) });
                foreach (var value in new string?[] { "AbC", "", null, "%_\\'" })
                {
                    needle = value;
                    Assert.Equal(seed.Select(row => new { row.Id, Match = Match(Encode(row.Converted), Encode(needle), 0, context) }), await Read(converted, async));
                }
                stage("unsupported overloads fail in predicates; final client projection remains allowed");
                IQueryable[] unsupported =
                [
                    rows.Where(row => row.Required.Contains("a", StringComparison.Ordinal)),
                    rows.Where(row => row.Required.StartsWith("a", StringComparison.OrdinalIgnoreCase)),
                    rows.Where(row => row.Required.EndsWith("a", StringComparison.Ordinal)),
                    rows.Where(row => row.Required.Contains('a', StringComparison.Ordinal)),
                    rows.Where(row => row.Required.ToLowerInvariant() == "a"),
                    rows.Where(row => row.Required.ToUpper(CultureInfo.InvariantCulture) == "a"),
                    rows.Where(row => row.BinaryConverted!.Remove(1) == "a"),
                    rows.Where(row => row.BinaryConverted!.ToLower() == "a"),
                    rows.Where(row => row.BinaryConverted!.Substring(0, 1) == "a")
                ];
                foreach (var rejected in unsupported)
                {
                    Assert.Contains("could not be translated", Assert.Throws<InvalidOperationException>(() => rejected.ToQueryString()).Message);
                }
                Assert.Equal(seed.Select(row => row.Required.ToUpperInvariant()), await Read(rows.OrderBy(row => row.Id).Select(row => row.Required.ToUpperInvariant()), async));
                Assert.Equal(8, async ? await rows.CountAsync() : rows.Count());
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task Ordinary_lob_functions_search_and_large_substrings_preserve_native_text(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => await WithTable(mode, async, nulls, async (context, seed, stage) =>
            {
                stage("ordinary LOB case/trim/length/substring and literal searches");
                var rows = context.Set<Row>();
                var query = rows.OrderBy(row => row.Id).Select(row => new
                {
                    row.Id, Lower = (string?)row.Large!.ToLower(), Trim = (string?)row.Large!.Trim(), Length = (int?)row.Large!.Length,
                    Part = (string?)row.Large!.Substring(1, 2), Contains = row.Large!.Contains("海🌊"), Ends = row.Large!.EndsWith("end")
                });
                Assert.Equal(seed.Select(row => new
                {
                    row.Id, Lower = row.Large?.ToLowerInvariant(), Trim = row.Large?.Trim(' '), Length = Characters(row.Large),
                    Part = Substring(row.Large, 1, 2, mode, true), Contains = Match(row.Large, "海🌊", 0, context), Ends = Match(row.Large, "end", 2, context)
                }), await Read(query, async));
                var emptyResults = rows.OrderBy(row => row.Id).Select(row => new
                {
                    row.Id, Empty = (string?)row.Large!.Substring(0, 0), Far = (string?)row.Large!.Substring(1000),
                    TrimNull = row.Large!.Trim() == null, TrimLength = (int?)row.Large!.Trim().Length
                });
                Assert.Equal(seed.Select(row => new
                {
                    row.Id, Empty = Substring(row.Large, 0, 0, mode, true), Far = Substring(row.Large, 1000, null, mode, true),
                    TrimNull = row.Large is null, TrimLength = Characters(row.Large?.Trim(' '))
                }), await Read(emptyResults, async));
                stage("LOB suffix captured empty/null/longer needles, columns and negation");
                string? needle = "first";
                var suffix = rows.OrderBy(row => row.Id).Select(row => new
                {
                    row.Id, Match = row.Large!.EndsWith(needle!), Column = row.Large!.EndsWith(row.Needle!),
                    Empty = row.Large!.EndsWith("")
                });
                foreach (var value in new string?[] { "end", "AbC ", "abcend ", "海🌊", "", null })
                {
                    needle = value;
                    Assert.Equal(seed.Select(row => new
                    {
                        row.Id, Match = Match(row.Large, needle, 2, context), Column = Match(row.Large, row.Needle, 2, context),
                        Empty = row.Large is not null
                    }), await Read(suffix, async));
                    Assert.Equal(seed.Where(row => !Match(row.Large, needle, 2, context)).Select(row => row.Id),
                        await Read(rows.Where(row => !row.Large!.EndsWith(needle!)).OrderBy(row => row.Id).Select(row => row.Id), async));
                }
                var large = " " + new string('a', 4001) + "海🌊END ";
                stage("parameter-written LOB over 4000 characters");
                var row = async ? await rows.AsTracking().SingleAsync(value => value.Id == 1) : rows.AsTracking().Single(value => value.Id == 1);
                row.Large = large;
                Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                context.ChangeTracker.Clear();
                var tail = rows.Where(value => value.Id == 1).Select(value => new
                {
                    Part = (string?)value.Large!.Substring(4000), Length = value.Large.Length,
                    Contains = value.Large.Contains("海🌊"), Ends = value.Large.TrimEnd().EndsWith("END"),
                    Lower = (string?)value.Large.ToLower(), Trim = (string?)value.Large.Trim(),
                    Prefix = (string?)value.Large.Substring(0, 4002)
                });
                var result = Assert.Single(await Read(tail, async));
                Assert.Equal(Substring(large, 4000, null, mode), result.Part);
                Assert.Equal(Characters(large), result.Length);
                Assert.True(result.Contains);
                Assert.True(result.Ends);
                Assert.Equal(large.ToLowerInvariant(), result.Lower);
                Assert.Equal(large.Trim(' '), result.Trim);
                Assert.Equal(Substring(large, 0, 4002, mode, true), result.Prefix);
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task Explicit_trim_removes_character_sets_with_refreshed_arrays_columns_and_text_families(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => await WithTable(mode, async, nulls, async (context, seed, stage) =>
            {
                var rows = context.Set<Row>();
                char[]? characters = ['A', 'b', 'c', ' '];
                var query = rows.OrderBy(row => row.Id).Select(row => new
                {
                    row.Id, Both = (string?)row.Plain!.Trim(characters), Start = (string?)row.Plain!.TrimStart(characters),
                    Forced = (string?)row.Plain!.Trim(EF.Constant(characters)),
                    End = (string?)row.Plain!.TrimEnd(characters), National = (string?)row.National!.Trim(characters),
                    Fixed = (string?)row.Fixed!.Trim(characters), NationalFixed = (string?)row.NationalFixed!.Trim(characters),
                    Lob = (string?)row.Large!.Trim(characters), Converted = (string?)row.Converted!.TrimStart(characters)
                });
                var filtered = rows.Where(row => row.Plain!.Trim(characters) == null).OrderBy(row => row.Id).Select(row => row.Id);
                foreach (var value in new char[]?[] { characters, ['A', 'b', 'c', ' '], ['a'], [], null,
                    ['海', 'd', 'n', 'e'], ['[', ']', '^', '-', '&', '\\', '\'', '\0'], [' '] })
                {
                    characters = value;
                    stage("explicit trim array refresh/default whitespace and text families");
                    await Check();
                    if (characters is { Length: > 0 })
                    {
                        characters[0] = 'c';
                        stage("explicit trim in-place array mutation");
                        await Check();
                    }
                }
                async Task Check()
                {
                    Assert.Equal(seed.Select(row => new
                    {
                        row.Id, Both = Normalize(row.Plain?.Trim(characters), mode), Start = Normalize(row.Plain?.TrimStart(characters), mode),
                        Forced = Normalize(row.Plain?.Trim(characters), mode),
                        End = Normalize(row.Plain?.TrimEnd(characters), mode), National = Normalize(row.National?.Trim(characters), mode),
                        Fixed = Normalize(row.Fixed?.Trim(characters), mode), NationalFixed = Normalize(row.NationalFixed?.Trim(characters), mode),
                        Lob = row.Large?.Trim(characters), Converted = Decode(Normalize(Encode(row.Converted)?.TrimStart(characters), mode))
                    }), await Read(query, async));
                    Assert.Equal(seed.Where(row => Normalize(row.Plain?.Trim(characters), mode) is null).Select(row => row.Id), await Read(filtered, async));
                }
                stage("explicit trim literals, inline nullable char columns and composition");
                var columns = rows.OrderBy(row => row.Id).Select(row => new
                {
                    row.Id, Single = (string?)row.Plain!.Trim(row.Character!.Value),
                    Set = (string?)row.Plain!.Trim(new[] { row.Character!.Value, ' ' }),
                    Literal = (string?)row.Plain!.TrimStart('A').TrimEnd(new[] { ' ', 'c' }),
                    EmptySet = (string?)row.Plain!.Trim(Array.Empty<char>()),
                    NullSet = (string?)row.Plain!.Trim((char[]?)null)
                });
                Assert.Equal(seed.Select(row => new
                {
                    row.Id, Single = row.Character is { } ch ? Normalize(row.Plain?.Trim(ch), mode) : null,
                    Set = row.Character is { } ch2 ? Normalize(row.Plain?.Trim(ch2, ' '), mode) : null,
                    Literal = Normalize(row.Plain?.TrimStart('A').TrimEnd(' ', 'c'), mode),
                    EmptySet = Normalize(row.Plain?.Trim(), mode), NullSet = Normalize(row.Plain?.Trim(), mode)
                }), await Read(columns, async));
                var entity = async ? await rows.AsTracking().SingleAsync(row => row.Id == 1) : rows.AsTracking().Single(row => row.Id == 1);
                var character = 'x';
                var source = "xxbodyxx";
                var dynamic = rows.Where(row => row.Id == 1).Select(row => new
                {
                    Single = (string?)row.Plain!.Trim(row.Character!.Value),
                    Set = (string?)row.Plain!.Trim(new[] { row.Character!.Value, ' ' }),
                    Parameter = (string?)row.Plain!.Trim(character), Start = (string?)row.Plain!.TrimStart(character),
                    End = (string?)row.Plain!.TrimEnd(character),
                    LiteralSource = "xxbodyxx".Trim(new[] { row.Character!.Value }),
                    ParameterSource = source.Trim(new[] { row.Character!.Value })
                });
                foreach (var value in new[] { 'x', 'A', '[', ']', '^', '-', '&', '\\', '\'', '\0', '海' })
                {
                    stage("explicit trim dynamic char class escaping and ASCII case sensitivity");
                    character = value;
                    entity.Plain = value + "X\n" + value;
                    entity.Character = value;
                    Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                    var result = Assert.Single(await Read(dynamic, async));
                    Assert.Equal("X\n", result.Single);
                    Assert.Equal("X\n", result.Set);
                    Assert.Equal("X\n", result.Parameter);
                    Assert.Equal("X\n" + value, result.Start);
                    Assert.Equal(value + "X\n", result.End);
                    Assert.Equal("xxbodyxx".Trim(value), result.LiteralSource);
                    Assert.Equal(source.Trim(value), result.ParameterSource);
                }
                stage("strict final-newline anchors and LOB result over 4000 characters");
                entity.Plain = "x\n";
                entity.Large = "[" + new string('v', 4001) + "海🌊]";
                Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                var large = rows.Where(row => row.Id == 1).Select(row => new
                {
                    End = (string?)row.Plain!.TrimEnd('x'), Lob = (string?)row.Large!.Trim(new[] { '[', ']' })
                });
                var last = Assert.Single(await Read(large, async));
                Assert.Equal("x\n", last.End);
                Assert.Equal(new string('v', 4001) + "海🌊", last.Lob);
            });

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task Pad_preserves_native_character_width_and_does_not_truncate(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => await WithTable(mode, async, nulls, async (context, seed, stage) =>
            {
                var rows = context.Set<Row>().OrderBy(row => row.Id);
                var width = 0;
                var token = 'x';
                var query = rows.Select(row => new
                {
                    row.Id, Left = (string?)row.Plain!.PadLeft(width), Right = (string?)row.Plain!.PadRight(width),
                    ExplicitLeft = (string?)row.Plain!.PadLeft(width, token), ExplicitRight = (string?)row.Plain!.PadRight(width, token),
                    Fixed = (string?)row.Fixed!.PadRight(width, token), National = (string?)row.NationalFixed!.PadLeft(width, token),
                    Converted = (string?)row.Converted!.PadLeft(width, token)
                });
                foreach (var input in new[] { (0, ' '), (3, 'x'), (10, '海'), (16, '\''), (10, '\\'), (-1, 'x'), (int.MinValue, 'x'), (2, ' ') })
                {
                    stage("Pad native character counts, shorter widths, constants, parameters and converted text");
                    (width, token) = input;
                    Assert.Equal(seed.Select(row => new
                    {
                        row.Id, Left = Padded(row.Plain, width, ' ', true), Right = Padded(row.Plain, width, ' ', false),
                        ExplicitLeft = Padded(row.Plain, width, token, true), ExplicitRight = Padded(row.Plain, width, token, false),
                        Fixed = Padded(row.Fixed, width, token, false), National = Padded(row.NationalFixed, width, token, true),
                        Converted = Decode(Padded(Encode(row.Converted), width, token, true))
                    }), await Read(query, async));
                }
                stage("Pad nullable width and char columns and query composition");
                var columns = rows.Select(row => new
                {
                    row.Id, Width = (string?)row.Plain!.PadRight(row.RemoveStart!.Value, '海'),
                    Token = (string?)row.Plain!.PadLeft(7, row.Character!.Value),
                    Composed = (string?)row.Plain!.PadRight(8, 'z').ToUpper(),
                    EmptyLob = (string?)row.Large!.Trim().PadLeft(2, 'x')
                });
                Assert.Equal(seed.Select(row => new
                {
                    row.Id, Width = Padded(row.Plain, row.RemoveStart, '海', false), Token = Padded(row.Plain, 7, row.Character, true),
                    Composed = Padded(row.Plain, 8, 'z', false)?.ToUpperInvariant(), EmptyLob = Padded(row.Large?.Trim(' '), 2, 'x', true)
                }), await Read(columns, async));
                Assert.Equal(seed.Where(row => Padded(row.Plain, 12, '海', true)?.StartsWith("海海", StringComparison.Ordinal) == true)
                    .Select(row => row.Id), await Read(rows.Where(row => row.Plain!.PadLeft(12, '海').StartsWith("海海"))
                        .Select(row => row.Id), async));

                stage("Pad LOB results beyond scalar text limits");
                var lob = Assert.Single(await Read(rows.Where(row => row.Id == 8).Select(row => row.Large!.PadLeft(4100, '海')), async));
                Assert.Equal(Padded(seed[7].Large, 4100, '海', true), lob);
            });

        private static string? Padded(string? source, int? width, char? token, bool left)
        {
            if (source is null || width is null or < 0 || token is null) return null;
            var difference = width.Value - source.EnumerateRunes().Count();
            if (difference <= 0) return source;
            var padding = new string(token.Value, difference);
            return left ? padding + source : source + padding;
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task Insert_preserves_native_character_bounds_empty_values_and_text_families(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => await WithTable(mode, async, nulls, async (context, seed, stage) =>
            {
                var rows = context.Set<Row>().OrderBy(row => row.Id);
                var start = 0;
                string? inserted = "x";
                var query = rows.Select(row => new
                {
                    row.Id, Plain = (string?)row.Plain!.Insert(start, inserted!),
                    National = (string?)row.National!.Insert(start, inserted!),
                    Fixed = (string?)row.Fixed!.Insert(start, inserted!),
                    NationalFixed = (string?)row.NationalFixed!.Insert(start, inserted!),
                    Converted = (string?)row.Converted!.Insert(start, inserted!)
                });
                var filtered = rows.Where(row => row.Plain!.Insert(start, inserted!) == null).Select(row => row.Id);
                foreach (var input in new (int Start, string? Value)[]
                {
                    (0, "海🌊"), (2, "'\\_"), (3, "longer than fixed"), (1, ""), (0, null),
                    (0, ""), (100, "x"), (-1, "x"), (int.MaxValue, "x"), (int.MinValue, "x"), (2, "x")
                })
                {
                    stage("Insert captured indices and values into ordinary, national, fixed and converted text");
                    (start, inserted) = input;
                    Assert.Equal(seed.Select(row => new
                    {
                        row.Id, Plain = Inserted(row.Plain, start, inserted, mode),
                        National = Inserted(row.National, start, inserted, mode),
                        Fixed = Inserted(row.Fixed, start, inserted, mode),
                        NationalFixed = Inserted(row.NationalFixed, start, inserted, mode),
                        Converted = Decode(Inserted(Encode(row.Converted), start, inserted, mode))
                    }), await Read(query, async));
                    Assert.Equal(seed.Where(row => Inserted(row.Plain, start, inserted, mode) is null).Select(row => row.Id),
                        await Read(filtered, async));
                }
                stage("Insert nullable columns, literal empty strings and both endpoints");
                var columns = rows.Select(row => new
                {
                    row.Id, Value = (string?)row.Plain!.Insert(row.RemoveStart!.Value, row.Needle!),
                    End = (string?)row.Plain!.Insert(row.Plain.Length, "🌊"),
                    Empty = (string?)row.Plain!.Insert(0, "").ToUpper(),
                    EmptyLob = (string?)row.Large!.Trim().Insert(0, "x")
                });
                Assert.Equal(seed.Select(row => new
                {
                    row.Id, Value = Inserted(row.Plain, row.RemoveStart, row.Needle, mode),
                    End = Inserted(row.Plain, Characters(row.Plain), "🌊", mode),
                    Empty = row.Plain?.ToUpperInvariant(), EmptyLob = Inserted(row.Large?.Trim(' '), 0, "x", mode)
                }), await Read(columns, async));

                stage("Insert into LOBs beyond scalar text limits");
                var rowWithLob = Assert.Single(await Read(rows.AsTracking().Where(row => row.Id == 1), async));
                rowWithLob.Large = new string('v', 4001) + "海🌊end";
                Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                var lob = Assert.Single(await Read(rows.Where(row => row.Id == 1).Select(row => row.Large!.Insert(4003, "X")), async));
                Assert.Equal(new string('v', 4001) + "海🌊Xend", lob);
            });

        private static string? Inserted(string? source, int? start, string? value, OceanBaseCompatibilityMode mode)
        {
            if (source is null || start is null or < 0 || value is null) return null;
            var characters = source.EnumerateRunes().ToArray();
            return start > characters.Length ? null : Normalize(string.Concat(characters.Take(start.Value))
                + value + string.Concat(characters.Skip(start.Value)), mode);
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task Replace_preserves_literal_tokens_nulls_and_variable_text(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => await WithTable(mode, async, nulls, async (context, seed, stage) =>
            {
                var rows = context.Set<Row>().OrderBy(row => row.Id);
                string? oldValue = "Ab";
                string? newValue = "longer";
                var query = rows.Select(row => new
                {
                    row.Id, Plain = (string?)row.Plain!.Replace(oldValue!, newValue),
                    National = (string?)row.National!.Replace(oldValue!, newValue),
                    Fixed = (string?)row.Fixed!.Replace(oldValue!, newValue),
                    NationalFixed = (string?)row.NationalFixed!.Replace(oldValue!, newValue),
                    Converted = (string?)row.Converted!.Replace(oldValue!, newValue)
                });
                foreach (var tokens in new (string? Old, string? New)[]
                {
                    ("Ab", "abcdefghijklmnop"), ("ab", "!"), ("%_\\'", "海🌊"), ("海🌊", "x"),
                    ("AbC", null), ("AbC", ""), ("", "x"), (null, "x"), ("Ab", "longer")
                })
                {
                    stage("Replace string tokens, case sensitivity, deletion, refreshed values and converted receivers");
                    (oldValue, newValue) = tokens;
                    Assert.Equal(seed.Select(row => new
                    {
                        row.Id, Plain = Replaced(row.Plain, oldValue, newValue, mode),
                        National = Replaced(row.National, oldValue, newValue, mode),
                        Fixed = Replaced(row.Fixed, oldValue, newValue, mode),
                        NationalFixed = Replaced(row.NationalFixed, oldValue, newValue, mode),
                        Converted = Decode(Replaced(Encode(row.Converted), oldValue, newValue, mode))
                    }), await Read(query, async));
                }
                stage("Replace char parameters, nullable columns and predicate composition");
                var oldCharacter = 'A';
                var chars = rows.Select(row => new
                {
                    row.Id, Captured = (string?)row.Plain!.Replace(oldCharacter, 'z'),
                    OldColumn = (string?)row.Plain!.Replace(row.Character!.Value, 'z'),
                    NewColumn = (string?)row.Plain!.Replace('A', row.Character!.Value),
                    OldString = (string?)row.Plain!.Replace(row.Needle!, "z"), NewString = (string?)row.Plain!.Replace("Ab", row.Other)
                });
                foreach (var character in new[] { 'A', '海', '\'', '\\' })
                {
                    oldCharacter = character;
                    Assert.Equal(seed.Select(row => new
                    {
                        row.Id, Captured = Replaced(row.Plain, character.ToString(), "z", mode),
                        OldColumn = Replaced(row.Plain, row.Character?.ToString(), "z", mode),
                        NewColumn = row.Character is null ? null : Replaced(row.Plain, "A", row.Character.ToString(), mode),
                        OldString = Replaced(row.Plain, row.Needle, "z", mode), NewString = Replaced(row.Plain, "Ab", row.Other, mode)
                    }), await Read(chars, async));
                    Assert.Equal(seed.Where(row => Replaced(row.Plain, character.ToString(), "z", mode)?.Contains('z') == true)
                        .Select(row => row.Id), await Read(rows.Where(row => row.Plain!.Replace(oldCharacter, 'z').Contains('z'))
                            .Select(row => row.Id), async));
                }
                stage("Replace expands LOBs and finds tokens beyond scalar text limits");
                var rowWithLob = Assert.Single(await Read(rows.AsTracking().Where(row => row.Id == 1), async));
                rowWithLob.Large = new string('v', 4001) + "海🌊end";
                Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                var lob = Assert.Single(await Read(rows.Where(row => row.Id == 1).Select(row => row.Large!.Replace("海🌊", "🌊🌊🌊")), async));
                Assert.Equal(new string('v', 4001) + "🌊🌊🌊end", lob);
            });

        private static string? Replaced(string? source, string? oldValue, string? newValue, OceanBaseCompatibilityMode mode)
            => source is null || string.IsNullOrEmpty(oldValue) ? null : Normalize(source.Replace(oldValue, newValue), mode);

        [Theory]
        [MemberData(nameof(Cases))]
        public async Task IndexOf_preserves_native_positions_ranges_nulls_and_contextual_search_converters(
            OceanBaseCompatibilityMode mode, bool async, bool nulls)
            => await WithTable(mode, async, nulls, async (context, seed, stage) =>
            {
                var rows = context.Set<Row>();
                string? needle = "Ab";
                var start = 0;
                var count = 2;
                var query = rows.OrderBy(row => row.Id).Select(row => new
                {
                    row.Id, All = (int?)row.Plain!.IndexOf(needle!), From = (int?)row.Plain!.IndexOf(needle!, start),
                    Range = (int?)row.Plain!.IndexOf(needle!, start, count), National = (int?)row.National!.IndexOf(needle!),
                    Fixed = (int?)row.Fixed!.IndexOf(needle!), NationalFixed = (int?)row.NationalFixed!.IndexOf(needle!),
                    Lob = (int?)row.Large!.IndexOf(needle!, start, count), Converted = (int?)row.Converted!.IndexOf(needle!, start, count)
                });
                var filtered = rows.Where(row => row.Plain!.IndexOf(needle!, start, count) >= 0).OrderBy(row => row.Id).Select(row => row.Id);
                foreach (var value in new (string? Needle, int Start, int Count)[]
                {
                    ("Ab", 0, 5), ("Ab", 0, 1), ("C", 2, 1), ("a", 1, 3), ("%_\\'", 0, 10), ("海🌊", 0, 20),
                    ("missing", 0, 0), ("<null>", 0, 2),
                    ("", 0, 0), ("", 3, 0), (null, 0, 2), ("end", 1, 7), (" ", 0, 4),
                    ("A", -1, 3), ("A", 0, -1), ("", int.MaxValue, int.MaxValue), ("x", int.MinValue, int.MinValue),
                    ("", 0, 20), ("Ab", 0, 5)
                })
                {
                    (needle, start, count) = value;
                    stage("IndexOf scalar refresh, ranges, native text families and converters");
                    Assert.Equal(seed.Select(row => new
                    {
                        row.Id, All = Index(row.Plain, needle, 0, null, context), From = Index(row.Plain, needle, start, null, context),
                        Range = Index(row.Plain, needle, start, count, context), National = Index(row.National, needle, 0, null, context),
                        Fixed = Index(row.Fixed, needle, 0, null, context), NationalFixed = Index(row.NationalFixed, needle, 0, null, context),
                        Lob = Index(row.Large, needle, start, count, context), Converted = Index(Encode(row.Converted), Encode(needle), start, count, context)
                    }), await Read(query, async));
                    Assert.Equal(seed.Where(row => Index(row.Plain, needle, start, count, context) >= 0).Select(row => row.Id), await Read(filtered, async));
                }
                var character = '海';
                var chars = rows.OrderBy(row => row.Id).Select(row => new
                {
                    row.Id, All = (int?)row.Plain!.IndexOf(character), From = (int?)row.Plain!.IndexOf(character, start),
                    Range = (int?)row.Plain!.IndexOf(character, start, count), Converted = (int?)row.Converted!.IndexOf(character)
                });
                start = 1; count = 3;
                foreach (var value in new[] { '海', '%', '\\', '\'', 'A', ' ', 'd' })
                {
                    character = value;
                    stage("IndexOf all char signatures and contextual char-to-text mappings");
                    Assert.Equal(seed.Select(row => new
                    {
                        row.Id, All = Index(row.Plain, value.ToString(), 0, null, context), From = Index(row.Plain, value.ToString(), start, null, context),
                        Range = Index(row.Plain, value.ToString(), start, count, context), Converted = Index(Encode(row.Converted), Encode(value.ToString()), 0, null, context)
                    }), await Read(chars, async));
                }
                if (mode == OceanBaseCompatibilityMode.MySql)
                {
                    stage("IndexOf NUL character with native INSTR collation weights");
                    character = '\0';
                    var table = context.GetService<ISqlGenerationHelper>().DelimitIdentifier(context.Table);
                    var sql = $"SELECT INSTR(`Plain`, {{0}}) AS `Value` FROM {table} WHERE `Id` = 1";
                    var native = Assert.Single(await Read(context.Database.SqlQueryRaw<int>(sql, "\0"), async));
                    Assert.Equal(native == 0 ? -1 : native - 1, Assert.Single(await Read(chars.Where(row => row.Id == 1), async)).All);
                }
                stage("IndexOf column needles, independently nullable bounds, composition and empty CLOB windows");
                var columns = rows.OrderBy(row => row.Id).Select(row => new
                {
                    row.Id, Text = (int?)row.Plain!.IndexOf(row.Needle!), Character = (int?)row.Plain!.IndexOf(row.Character!.Value),
                    Bounds = (int?)row.Plain!.IndexOf(row.Needle!, row.RemoveStart!.Value, row.RemoveCount!.Value),
                    Nested = (int?)(row.Plain ?? row.Required).ToUpper().IndexOf('A'),
                    Empty = (int?)row.Large!.Trim().IndexOf(""), Literal = (int?)row.Plain!.IndexOf("", 0, 0),
                    ConvertedNull = (int?)row.Converted!.IndexOf("<null>")
                });
                Assert.Equal(seed.Select(row => new
                {
                    row.Id, Text = Index(row.Plain, row.Needle, 0, null, context), Character = Index(row.Plain, row.Character?.ToString(), 0, null, context),
                    Bounds = row.RemoveStart is null || row.RemoveCount is null ? null : Index(row.Plain, row.Needle, row.RemoveStart.Value, row.RemoveCount, context),
                    Nested = Index((row.Plain ?? row.Required).ToUpperInvariant(), "A", 0, null, context),
                    Empty = Index(row.Large?.Trim(' '), "", 0, null, context), Literal = Index(row.Plain, "", 0, 0, context),
                    ConvertedNull = (int?)null
                }), await Read(columns, async));

                stage("IndexOf finds supplementary characters beyond scalar text limits");
                var rowWithLob = Assert.Single(await Read(rows.AsTracking().Where(row => row.Id == 1), async));
                rowWithLob.Large = new string('v', 4001) + "海🌊end";
                Assert.Equal(1, async ? await context.SaveChangesAsync() : context.SaveChanges());
                var lob = Assert.Single(await Read(rows.Where(row => row.Id == 1).Select(row => new
                {
                    All = (int?)row.Large!.IndexOf("🌊"),
                    Range = (int?)row.Large!.IndexOf("🌊end", 1, 4005),
                    Outside = (int?)row.Large!.IndexOf("🌊end", 1, 4004)
                }), async));
                Assert.Equal(4002, lob.All);
                Assert.Equal(4002, lob.Range);
                Assert.Equal(-1, lob.Outside);
            });

        private static int? Index(string? source, string? needle, int start, int? count, QueryContext context)
        {
            if (source is null || needle is null) return null;
            var characters = source.EnumerateRunes().ToArray();
            var length = count ?? characters.Length - (long)start;
            if (start < 0 || start > characters.Length || length < 0 || length > characters.Length - (long)start) return null;
            var window = string.Concat(characters.Skip(start).Take((int)length));
            var position = window.IndexOf(needle, context.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            return position < 0 ? -1 : start + window[..position].EnumerateRunes().Count();
        }

        private static bool Match(string? source, string? needle, int operation, QueryContext context)
        {
            if (source is null || needle is null) return false;
            if (needle.Length == 0) return true;
            var comparison = context.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (operation == 2)
            {
                var count = needle.EnumerateRunes().Count();
                return source.EnumerateRunes().Count() >= count
                    && EqualText(string.Concat(source.EnumerateRunes().TakeLast(count)), needle, context);
            }
            return operation == 0 ? source.Contains(needle, comparison)
                : source.StartsWith(needle, comparison);
        }

        private static bool EqualText(string? left, string? right, QueryContext context)
        {
            if (left is null || right is null) return left == right;
            if (context.Mode == OceanBaseCompatibilityMode.MySql)
            {
                // The accepted unicode_ci profile equates these whitespace characters in equality, not INSTR.
                if (context.IgnoreCase)
                {
                    left = left.Replace('\u00a0', ' ').Replace('\u2003', ' ');
                    right = right.Replace('\u00a0', ' ').Replace('\u2003', ' ');
                }
                left = left.TrimEnd(' '); right = right.TrimEnd(' ');
            }
            return string.Equals(left, right, context.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }

        private static string? Normalize(string? value, OceanBaseCompatibilityMode mode)
            => mode == OceanBaseCompatibilityMode.Oracle && value == "" ? null : value;
        private static int? Characters(string? value) => value?.EnumerateRunes().Count();
        private static string? Encode(string? value) => value is null or "<null>" ? null : value + "!";
        private static string? Decode(string? value) => value is null ? null : value[..Math.Max(0, value.Length - 1)];

        private static string? Remove(string? value, int? start, int? count, OceanBaseCompatibilityMode mode, bool ranged)
        {
            if (value is null || start is null || start < 0) return null;
            var characters = value.EnumerateRunes().ToArray();
            if (start > characters.Length || ranged && (count is null || count < 0 || count > characters.Length - start)) return null;
            var result = string.Concat(characters.Take(start.Value));
            if (ranged) result += string.Concat(characters.Skip(start.Value + count!.Value));
            return Normalize(result, mode);
        }

        private static string? Substring(string? source, int start, int? length, OceanBaseCompatibilityMode mode, bool lob = false)
        {
            if (source is null) return null;
            var characters = source.EnumerateRunes().Select(rune => rune.ToString()).ToArray();
            var position = (long)start + 1;
            if (position == 0 && mode == OceanBaseCompatibilityMode.Oracle) position = 1;
            var index = position > 0 ? position - 1 : characters.Length + position;
            if (length <= 0) return mode == OceanBaseCompatibilityMode.Oracle ? null : "";
            if (position == 0 || index < 0 || index >= characters.Length)
                return mode == OceanBaseCompatibilityMode.Oracle && !lob ? null : "";
            return Normalize(string.Concat(characters.Skip((int)index).Take(length ?? int.MaxValue)), mode);
        }

        private static Task<T[]> Read<T>(IQueryable<T> query, bool async) => async ? query.ToArrayAsync() : Task.FromResult(query.ToArray());

        private Task WithTable(OceanBaseCompatibilityMode mode, bool async, bool nulls, Func<QueryContext, Row[], Action<string>, Task> run)
            => LiveStorageTable.WithTable<QueryContext>(mode, async, false, (options, table) =>
            {
                var builder = new DbContextOptionsBuilder<QueryContext>(options).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
                    .ReplaceService<IModelCacheKeyFactory, CacheKey>();
                var extension = builder.Options.Extensions.OfType<RelationalOptionsExtension>().Single().WithUseRelationalNulls(nulls);
                ((IDbContextOptionsBuilderInfrastructure)builder).AddOrUpdateExtension(extension);
                return new QueryContext(builder.Options, mode, table);
            }, async (context, _, stage) =>
            {
                stage("read native comparison profile and verify seeded store values");
                if (mode == OceanBaseCompatibilityMode.MySql)
                {
                    const string sql = "SELECT collation_name AS `Value` FROM information_schema.columns WHERE table_schema = DATABASE() AND table_name = {0} AND column_name = 'Plain'";
                    var collation = Assert.Single(await Read(context.Database.SqlQueryRaw<string>(sql, context.Table), async));
                    Assert.StartsWith("utf8mb4_", collation, StringComparison.OrdinalIgnoreCase);
                    context.IgnoreCase = collation.Equals("utf8mb4_unicode_ci", StringComparison.OrdinalIgnoreCase);
                    Assert.True(context.IgnoreCase || collation.EndsWith("_bin", StringComparison.OrdinalIgnoreCase));
                    output.WriteLine("MySQL ordinary column collation=" + collation);
                }
                else
                {
                    var folded = Assert.Single(await Read(context.Database.SqlQueryRaw<int>("SELECT CASE WHEN 'A' = 'a' THEN 1 ELSE 0 END AS \"Value\" FROM DUAL"), async));
                    Assert.Equal(0, folded);
                    output.WriteLine("Oracle ordinary comparison probe: ASCII case-sensitive.");
                }
                var seed = Seed().Select(row => row with
                {
                    Plain = Normalize(row.Plain, mode), National = Normalize(row.National, mode), Needle = Normalize(row.Needle, mode),
                    Other = Normalize(row.Other, mode), Large = Normalize(row.Large, mode),
                    Fixed = StoredFixed(row.Fixed, mode), NationalFixed = StoredFixed(row.NationalFixed, mode)
                }).ToArray();
                Assert.Equal(seed, await Read(context.Set<Row>().OrderBy(row => row.Id), async));
                await run(context, seed, stage);
            });

        private static string? StoredFixed(string? value, OceanBaseCompatibilityMode mode)
        {
            value = Normalize(value, mode);
            return value is null ? null : mode == OceanBaseCompatibilityMode.MySql ? value.TrimEnd(' ')
                : value + new string(' ', 8 - Characters(value)!.Value);
        }

        private static Row[] Seed()
        {
            string?[] texts = [" AbC%_\\'海🌊 ", null, "", "    ", "\tAbC\r\n\u00a0\u2003", "AbC", "abc ", "海🌊海end"];
            string?[] needles = ["%_\\'", null, "", " ", "\t", "Ab", "abc ", "海🌊"];
            string?[] fixedTexts = ["AbC", null, "", "X ", " ab", "abc", "abc ", "海🌊"];
            char?[] characters = ['%', 'A', null, ' ', '\t', 'A', ' ', '海'];
            return texts.Select((text, index) => new Row
            {
                Id = index + 1, Plain = text, Required = string.IsNullOrEmpty(text) ? " " : text, National = text,
                Fixed = fixedTexts[index], NationalFixed = fixedTexts[index], Needle = needles[index], Other = index % 2 == 0 ? "tail" : null,
                Converted = text, BinaryConverted = text == "" ? "binary" : text, Large = text, Start = index - 2, Length = index % 4 - 1,
                Character = characters[index], RemoveStart = index == 5 ? null : index - 2, RemoveCount = index == 6 ? null : index % 4 - 1
            }).ToArray();
        }

        private sealed class QueryContext(DbContextOptions<QueryContext> options, OceanBaseCompatibilityMode mode, string table) : DbContext(options)
        {
            public OceanBaseCompatibilityMode Mode => mode;
            public string Table => table;
            public bool IgnoreCase { get; set; }
            protected override void OnModelCreating(ModelBuilder model)
            {
                var row = model.Entity<Row>();
                row.ToTable(table);
                row.Property(value => value.Id).ValueGeneratedNever();
                row.Property(value => value.Plain).IsUnicode(false).HasMaxLength(64);
                row.Property(value => value.Required).IsUnicode(false).HasMaxLength(64);
                row.Property(value => value.National).IsUnicode().HasMaxLength(64);
                row.Property(value => value.Needle).IsUnicode(false).HasMaxLength(64);
                row.Property(value => value.Other).IsUnicode(false).HasMaxLength(64);
                row.Property(value => value.Fixed).IsUnicode(false).IsFixedLength().HasMaxLength(8);
                row.Property(value => value.NationalFixed).IsUnicode().IsFixedLength().HasMaxLength(8);
                row.Property(value => value.Converted).HasConversion(value => value == "<null>" ? null : value + "!",
                    value => value!.Substring(0, value.Length - 1)).IsUnicode(false).HasMaxLength(80);
                row.Property(value => value.BinaryConverted).HasConversion(value => Encoding.UTF8.GetBytes(value!), value => Encoding.UTF8.GetString(value)).HasMaxLength(128);
                row.Property(value => value.Large).IsUnicode(false).HasColumnType(mode == OceanBaseCompatibilityMode.MySql ? "LONGTEXT" : "CLOB");
                row.Property(value => value.Character).HasConversion<string>().IsUnicode(false).HasMaxLength(1);
                row.HasData(Seed());
            }
        }

        private sealed class CacheKey : IModelCacheKeyFactory
        {
            public object Create(DbContext context, bool designTime) => (context.GetType(), ((QueryContext)context).Mode, ((QueryContext)context).Table, designTime);
        }

        private sealed record Row
        {
            public int Id { get; set; }
            public string? Plain { get; set; }
            public string Required { get; set; } = "";
            public string? National { get; set; }
            public string? Fixed { get; set; }
            public string? NationalFixed { get; set; }
            public string? Needle { get; set; }
            public string? Other { get; set; }
            public string? Converted { get; set; }
            public string? BinaryConverted { get; set; }
            public string? Large { get; set; }
            public int Start { get; set; }
            public int Length { get; set; }
            public char? Character { get; set; }
            public int? RemoveStart { get; set; }
            public int? RemoveCount { get; set; }
        }
    }
}
