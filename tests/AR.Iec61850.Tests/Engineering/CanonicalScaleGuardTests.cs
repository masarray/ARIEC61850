using System.Reflection;
using AR.Iec61850.Engineering.Canonical;

namespace AR.Iec61850.Tests.Engineering;

public sealed class CanonicalScaleGuardTests
{
    [Fact]
    public void Hot_Signal_Row_Contains_No_Managed_Reference_Fields()
    {
        AssertNoManagedReferenceFields(typeof(CanonicalSignalRow), new HashSet<Type>());
    }

    [Fact]
    public void Hot_Signal_Table_Stores_One_Million_Rows_Without_Per_Row_Object_Allocation()
    {
        const int signalCount = 1_000_000;
        const long allocationBudgetBytes = 128L * 1024L * 1024L;

        var before = GC.GetAllocatedBytesForCurrentThread();
        var rows = new CanonicalSignalRow[signalCount];
        for (var i = 0; i < rows.Length; i++)
        {
            rows[i] = new CanonicalSignalRow(
                i,
                i % 10_000,
                new CanonicalSymbol(0),
                new CanonicalSymbol(1),
                new CanonicalSymbol(2),
                new CanonicalSymbol(3),
                new CanonicalSymbol(4),
                new CanonicalSymbol(5),
                new CanonicalProvenance(CanonicalEvidenceSource.LiveMms, CanonicalConfidence.Exact));
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(signalCount, rows.Length);
        Assert.True(
            allocated < allocationBudgetBytes,
            $"One-million-row canonical hot table allocated {allocated:N0} bytes; budget is {allocationBudgetBytes:N0} bytes. The hot table must remain struct-backed without one managed object per signal.");
    }

    private static void AssertNoManagedReferenceFields(Type type, ISet<Type> visited)
    {
        if (!visited.Add(type))
            return;

        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            var fieldType = field.FieldType;
            Assert.True(
                fieldType.IsValueType,
                $"{type.FullName}.{field.Name} is a managed reference field ({fieldType.FullName}); high-cardinality canonical rows must remain value-only.");

            if (!fieldType.IsPrimitive && !fieldType.IsEnum)
                AssertNoManagedReferenceFields(fieldType, visited);
        }
    }
}
