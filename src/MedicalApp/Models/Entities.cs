using MiniOrm.Attributes;

namespace MedicalApp.Models;

public enum Gender
{
    M,
    Z,
    Ostalo
}

public enum ExamType
{
    CT,
    MR,
    ULTRA,
    EKG,
    ECHO,
    OKO,
    DERM,
    DENTA,
    MAMMO,
    EEG
}

[Table("addresses")]
public sealed class Address
{
    [Key, Identity]
    [Column("id", PgType = "INT")]
    public int Id { get; set; }

    [NotNull]
    [Column("street", PgType = "VARCHAR", Length = 150)]
    public string Street { get; set; } = "";

    [NotNull]
    [Column("city", PgType = "VARCHAR", Length = 80)]
    public string City { get; set; } = "";

    [Column("postal_code", PgType = "CHAR", Length = 10)]
    public string? PostalCode { get; set; }

    [NotNull]
    [SqlDefault("'Hrvatska'")]
    [Column("country", PgType = "VARCHAR", Length = 80)]
    public string Country { get; set; } = "Hrvatska";
}

[Table("doctors")]
public sealed class Doctor
{
    [Key, Identity]
    [Column("id", PgType = "INT")]
    public int Id { get; set; }

    [NotNull]
    [Column("first_name", PgType = "VARCHAR", Length = 80)]
    public string FirstName { get; set; } = "";

    [NotNull]
    [Column("last_name", PgType = "VARCHAR", Length = 80)]
    public string LastName { get; set; } = "";

    [NotNull]
    [Column("specialization", PgType = "VARCHAR", Length = 120)]
    public string Specialization { get; set; } = "";

    [HasMany(nameof(Examination.DoctorId))]
    public IList<Examination>? Examinations { get; set; }

    public override string ToString() => $"{Id}: {FirstName} {LastName} ({Specialization})";
}

[Table("patients")]
public sealed class Patient
{
    [Key, Identity]
    [Column("id", PgType = "INT")]
    public int Id { get; set; }

    [NotNull]
    [Column("first_name", PgType = "VARCHAR", Length = 80)]
    public string FirstName { get; set; } = "";

    [NotNull]
    [Column("last_name", PgType = "VARCHAR", Length = 80)]
    public string LastName { get; set; } = "";

    [NotNull, Unique]
    [Column("oib", PgType = "CHAR", Length = 11)]
    public string Oib { get; set; } = "";

    [NotNull]
    [Column("date_of_birth", PgType = "TIMESTAMP WITHOUT TIMEZONE")]
    public DateTime DateOfBirth { get; set; }

    [NotNull]
    [Column("gender", PgType = "VARCHAR", Length = 16)]
    public Gender Gender { get; set; }

    [Column("weight_kg", PgType = "FLOAT")]
    public double? WeightKg { get; set; }

    [NotNull]
    [ForeignKey("addresses")]
    [Column("residence_address_id", PgType = "INT")]
    public int ResidenceAddressId { get; set; }

    [NotNull]
    [ForeignKey("addresses")]
    [Column("permanent_address_id", PgType = "INT")]
    public int PermanentAddressId { get; set; }

    [NotNull]
    [SqlDefault("NOW()")]
    [Column("created_at", PgType = "TIMESTAMP WITH TIMEZONE")]
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    [BelongsTo(nameof(ResidenceAddressId))]
    public Address? ResidenceAddress { get; set; }

    [BelongsTo(nameof(PermanentAddressId))]
    public Address? PermanentAddress { get; set; }

    [HasMany(nameof(Illness.PatientId))]
    public IList<Illness>? Illnesses { get; set; }

    [HasMany(nameof(Examination.PatientId))]
    public IList<Examination>? Examinations { get; set; }

    public override string ToString() => $"{Id}: {FirstName} {LastName}  OIB={Oib}";
}

[Table("illnesses")]
public sealed class Illness
{
    [Key, Identity]
    [Column("id", PgType = "INT")]
    public int Id { get; set; }

    [NotNull]
    [ForeignKey("patients")]
    [Column("patient_id", PgType = "INT")]
    public int PatientId { get; set; }

    [NotNull]
    [Column("diagnosis", PgType = "VARCHAR", Length = 200)]
    public string Diagnosis { get; set; } = "";

    [Column("notes", PgType = "TEXT")]
    public string? Notes { get; set; }

    [NotNull]
    [Column("started_on", PgType = "TIMESTAMP WITHOUT TIMEZONE")]
    public DateTime StartedOn { get; set; }

    [Column("ended_on", PgType = "TIMESTAMP WITHOUT TIMEZONE")]
    public DateTime? EndedOn { get; set; }

    [BelongsTo(nameof(PatientId))]
    public Patient? Patient { get; set; }

    [HasMany(nameof(Medication.IllnessId))]
    public IList<Medication>? Medications { get; set; }

    public override string ToString()
    {
        var end = EndedOn is null ? "u tijeku" : EndedOn.Value.ToString("yyyy-MM-dd");
        return $"{Id}: {Diagnosis} ({StartedOn:yyyy-MM-dd} – {end})";
    }
}

[Table("medications")]
public sealed class Medication
{
    [Key, Identity]
    [Column("id", PgType = "INT")]
    public int Id { get; set; }

    [NotNull]
    [ForeignKey("illnesses")]
    [Column("illness_id", PgType = "INT")]
    public int IllnessId { get; set; }

    [NotNull]
    [Column("name", PgType = "VARCHAR", Length = 120)]
    public string Name { get; set; } = "";

    [NotNull]
    [Column("dose", PgType = "DECIMAL", Precision = 12, Scale = 3)]
    public decimal Dose { get; set; }

    [NotNull]
    [Column("unit", PgType = "VARCHAR", Length = 40)]
    public string Unit { get; set; } = "mg";

    [NotNull]
    [Column("frequency", PgType = "VARCHAR", Length = 80)]
    public string Frequency { get; set; } = "";

    [NotNull]
    [Column("is_current", PgType = "INT")]
    [SqlDefault("1")]
    public int IsCurrent { get; set; } = 1;

    [BelongsTo(nameof(IllnessId))]
    public Illness? Illness { get; set; }

    public override string ToString() =>
        $"{Id}: {Name} {Dose} {Unit}, {Frequency}" + (IsCurrent == 1 ? " [aktivan]" : " [neaktivan]");
}

[Table("examinations")]
public sealed class Examination
{
    [Key, Identity]
    [Column("id", PgType = "INT")]
    public int Id { get; set; }

    [NotNull]
    [ForeignKey("patients")]
    [Column("patient_id", PgType = "INT")]
    public int PatientId { get; set; }

    [NotNull]
    [ForeignKey("doctors")]
    [Column("doctor_id", PgType = "INT")]
    public int DoctorId { get; set; }

    [NotNull]
    [Column("exam_type", PgType = "VARCHAR", Length = 16)]
    public ExamType ExamType { get; set; }

    [NotNull]
    [Column("scheduled_at", PgType = "TIMESTAMP WITH TIMEZONE")]
    public DateTimeOffset ScheduledAt { get; set; }

    [Column("notes", PgType = "TEXT")]
    public string? Notes { get; set; }

    [BelongsTo(nameof(PatientId))]
    public Patient? Patient { get; set; }

    [BelongsTo(nameof(DoctorId))]
    public Doctor? Doctor { get; set; }

    public override string ToString() =>
        $"{Id}: {ExamType}  {ScheduledAt:yyyy-MM-dd HH:mm}  pacijent={PatientId} liječnik={DoctorId}";
}
