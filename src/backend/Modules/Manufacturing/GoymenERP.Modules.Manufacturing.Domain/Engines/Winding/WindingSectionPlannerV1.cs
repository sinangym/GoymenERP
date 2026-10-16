using System.Collections;

namespace GoymenERP.Modules.Manufacturing.Domain.Engines.Winding;

/// <summary>
/// Source-stamped values are captured by the plan/profile owner before this pure planner is called.
/// Every selected limit remains nullable here so missing data is explicit and fail-closed.
/// </summary>
public sealed record WindingSectionPlanInputV1(
    decimal? TotalPlannedKg,
    decimal? TargetKg,
    decimal? DiameterCapacityKg,
    decimal? MachineLimitKg,
    decimal? HandlingLimitKg);

public sealed record WindingSectionProposalV1(int SourceOrdinal, decimal PlannedKg, bool IsPositiveRemainder);

public sealed record WindingSectionPlanV1(
    string FormulaVersion,
    decimal EffectiveSectionLimitKg,
    decimal TotalPlannedKg,
    IReadOnlyList<WindingSectionProposalV1> Sections);

/// <summary>
/// M01.7/M02.6 pure winding-section planner. It does not infer mass from width, density or a current
/// Product record, and it creates no physical coil/label. The four selected limits are mandatory:
/// a missing limit is unavailable, never zero or unlimited. A positive remainder is another proposed
/// section, never scrap, trim or loss.
/// </summary>
public static class WindingSectionPlannerV1
{
    public const string FormulaVersion = "GDC-WND-SECTION/1 (D-M017=A)";
    public const string SectionCountOverflowCode = "winding_section_count_overflow";

    public static EngineResult<WindingSectionPlanV1> Plan(WindingSectionPlanInputV1 input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var inputs = new EngineInputs();
        var total = inputs.Positive(input.TotalPlannedKg, "totalPlannedKg");
        var target = inputs.Positive(input.TargetKg, "targetKg");
        var diameter = inputs.Positive(input.DiameterCapacityKg, "diameterCapacityKg");
        var machine = inputs.Positive(input.MachineLimitKg, "machineLimitKg");
        var handling = inputs.Positive(input.HandlingLimitKg, "handlingLimitKg");
        if (!inputs.IsComplete)
        {
            return inputs.Incomplete<WindingSectionPlanV1>();
        }

        var limit = Math.Min(Math.Min(target, diameter), Math.Min(machine, handling));
        decimal fullSectionCount;
        decimal remainder;
        try
        {
            fullSectionCount = decimal.Floor(total / limit);
            remainder = total % limit;
        }
        catch (OverflowException)
        {
            return EngineResult<WindingSectionPlanV1>.Refused(SectionCountOverflowCode, "totalPlannedKg");
        }

        var resultCount = fullSectionCount + (remainder > 0m ? 1m : 0m);
        if (resultCount > int.MaxValue)
        {
            return EngineResult<WindingSectionPlanV1>.Refused(SectionCountOverflowCode, "totalPlannedKg");
        }

        var sections = new WindingSectionProposalList((int)fullSectionCount, limit, remainder);
        return EngineResult<WindingSectionPlanV1>.Computed(
            new WindingSectionPlanV1(FormulaVersion, limit, total, sections));
    }

    private sealed class WindingSectionProposalList(int fullSectionCount, decimal limitKg, decimal remainderKg)
        : IReadOnlyList<WindingSectionProposalV1>
    {
        public int Count { get; } = checked(fullSectionCount + (remainderKg > 0m ? 1 : 0));

        public WindingSectionProposalV1 this[int index]
        {
            get
            {
                if ((uint)index >= (uint)Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                var isRemainder = index == fullSectionCount && remainderKg > 0m;
                return new WindingSectionProposalV1(index, isRemainder ? remainderKg : limitKg, isRemainder);
            }
        }

        public IEnumerator<WindingSectionProposalV1> GetEnumerator()
        {
            for (var index = 0; index < Count; index++)
            {
                yield return this[index];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
