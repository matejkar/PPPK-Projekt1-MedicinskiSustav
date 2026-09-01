using MedicalApp.Models;
using MiniOrm;

namespace MedicalApp;

public sealed class MedicalDbContext : MiniOrmContext
{
    public MedicalDbContext(string connectionString) : base(connectionString) { }

    public DbSet<Address> Addresses { get; set; } = null!;
    public DbSet<Doctor> Doctors { get; set; } = null!;
    public DbSet<Patient> Patients { get; set; } = null!;
    public DbSet<Illness> Illnesses { get; set; } = null!;
    public DbSet<Medication> Medications { get; set; } = null!;
    public DbSet<Examination> Examinations { get; set; } = null!;
}
