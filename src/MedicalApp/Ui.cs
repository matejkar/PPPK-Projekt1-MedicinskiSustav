using MedicalApp.Models;
using MiniOrm.Loading;

namespace MedicalApp;

internal static class ConsoleUi
{
    public static void Title(string text)
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 56));
        Console.WriteLine(text);
        Console.WriteLine(new string('=', 56));
    }

    public static string ReadRequired(string label)
    {
        while (true)
        {
            Console.Write($"{label}: ");
            var value = Console.ReadLine()?.Trim();
            if (!string.IsNullOrWhiteSpace(value))
                return value;
            Console.WriteLine("Vrijednost je obavezna.");
        }
    }

    public static string? ReadOptional(string label)
    {
        Console.Write($"{label} (Enter = prazno): ");
        var value = Console.ReadLine()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    public static int ReadInt(string label)
    {
        while (true)
        {
            Console.Write($"{label}: ");
            if (int.TryParse(Console.ReadLine(), out var n))
                return n;
            Console.WriteLine("Unesite cijeli broj.");
        }
    }

    public static decimal ReadDecimal(string label)
    {
        while (true)
        {
            Console.Write($"{label}: ");
            if (decimal.TryParse(Console.ReadLine(), out var n))
                return n;
            Console.WriteLine("Unesite broj.");
        }
    }

    public static double? ReadOptionalDouble(string label)
    {
        Console.Write($"{label} (Enter = prazno): ");
        var raw = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (double.TryParse(raw, out var n)) return n;
        Console.WriteLine("Ignoriran neispravan unos.");
        return null;
    }

    public static DateTime ReadDate(string label)
    {
        while (true)
        {
            Console.Write($"{label} (yyyy-MM-dd): ");
            if (DateTime.TryParse(Console.ReadLine(), out var d))
                return d.Date;
            Console.WriteLine("Neispravan datum.");
        }
    }

    public static DateTime? ReadOptionalDate(string label)
    {
        Console.Write($"{label} (yyyy-MM-dd, Enter = prazno): ");
        var raw = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (DateTime.TryParse(raw, out var d)) return d.Date;
        Console.WriteLine("Ignoriran neispravan unos.");
        return null;
    }

    public static DateTimeOffset ReadDateTimeOffset(string label)
    {
        while (true)
        {
            Console.Write($"{label} (yyyy-MM-dd HH:mm): ");
            if (DateTime.TryParse(Console.ReadLine(), out var d))
                return new DateTimeOffset(DateTime.SpecifyKind(d, DateTimeKind.Local));
            Console.WriteLine("Neispravan datum/vrijeme.");
        }
    }

    public static Gender ReadGender()
    {
        Console.WriteLine("Spol: 1=M  2=Z  3=Ostalo");
        return ReadInt("Odabir") switch
        {
            2 => Gender.Z,
            3 => Gender.Ostalo,
            _ => Gender.M
        };
    }

    public static ExamType ReadExamType()
    {
        var values = Enum.GetValues<ExamType>();
        for (var i = 0; i < values.Length; i++)
            Console.WriteLine($"  {i + 1}. {values[i]}");
        var n = ReadInt("Tip pregleda");
        if (n < 1 || n > values.Length) return ExamType.CT;
        return values[n - 1];
    }

    public static bool Confirm(string label)
    {
        Console.Write($"{label} (d/n): ");
        var raw = Console.ReadLine()?.Trim().ToLowerInvariant();
        return raw is "d" or "da" or "y";
    }
}

internal static class Menus
{
    public static void Patients(MedicalDbContext db)
    {
        while (true)
        {
            ConsoleUi.Title("Pacijenti");
            Console.WriteLine("1. Popis (filter po prezimenu)");
            Console.WriteLine("2. Detalj + eager load (adrese, bolesti, pregledi)");
            Console.WriteLine("3. Dodaj");
            Console.WriteLine("4. Uredi (change tracking)");
            Console.WriteLine("5. Obriši");
            Console.WriteLine("0. Natrag");
            switch (ConsoleUi.ReadInt("Odabir"))
            {
                case 0: return;
                case 1:
                    var last = ConsoleUi.ReadOptional("Prezime sadrži");
                    var q = db.Patients.OrderBy(p => p.LastName);
                    if (!string.IsNullOrWhiteSpace(last))
                        q = q.Where(p => p.LastName.Contains(last));
                    foreach (var p in q.ToList())
                        Console.WriteLine("  " + p);
                    break;
                case 2:
                    var id = ConsoleUi.ReadInt("Id pacijenta");
                    var patient = db.Patients
                        .Where(p => p.Id == id)
                        .Include(p => p.ResidenceAddress)
                        .Include(p => p.PermanentAddress)
                        .Include(p => p.Illnesses)
                        .ThenInclude<Illness, IList<Medication>?>(i => i.Medications)
                        .Include(p => p.Examinations)
                        .ThenInclude<Examination, Doctor?>(e => e.Doctor)
                        .FirstOrDefault();
                    if (patient is null) { Console.WriteLine("Nije pronađen."); break; }
                    PrintPatient(patient);
                    break;
                case 3:
                    CreatePatient(db);
                    break;
                case 4:
                    UpdatePatient(db);
                    break;
                case 5:
                    var delId = ConsoleUi.ReadInt("Id za brisanje");
                    var del = db.Patients.Find(delId);
                    if (del is null) { Console.WriteLine("Nije pronađen."); break; }
                    db.Patients.Remove(del);
                    db.SaveChanges();
                    Console.WriteLine("Obrisan.");
                    break;
            }
        }
    }

    public static void Illnesses(MedicalDbContext db)
    {
        while (true)
        {
            ConsoleUi.Title("Povijest bolesti");
            Console.WriteLine("1. Popis za pacijenta (lazy load lijekova)");
            Console.WriteLine("2. Dodaj");
            Console.WriteLine("3. Uredi");
            Console.WriteLine("4. Obriši");
            Console.WriteLine("0. Natrag");
            switch (ConsoleUi.ReadInt("Odabir"))
            {
                case 0: return;
                case 1:
                    var pid = ConsoleUi.ReadInt("Id pacijenta");
                    var items = db.Illnesses.Where(i => i.PatientId == pid).OrderByDescending(i => i.StartedOn).ToList();
                    foreach (var i in items)
                    {
                        Console.WriteLine("  " + i);
                        if (i.Medications is LazyLoadList<Medication> lazy)
                            Console.WriteLine($"     lazy loaded lijekovi: {lazy.Count}  (IsLoaded={lazy.IsLoaded})");
                        else
                            Console.WriteLine($"     lijekovi: {i.Medications?.Count ?? 0}");
                    }
                    break;
                case 2:
                    db.Illnesses.Add(new Illness
                    {
                        PatientId = ConsoleUi.ReadInt("Id pacijenta"),
                        Diagnosis = ConsoleUi.ReadRequired("Dijagnoza"),
                        Notes = ConsoleUi.ReadOptional("Bilješke"),
                        StartedOn = ConsoleUi.ReadDate("Početak"),
                        EndedOn = ConsoleUi.ReadOptionalDate("Kraj")
                    });
                    db.SaveChanges();
                    Console.WriteLine("Spremljeno.");
                    break;
                case 3:
                    var iid = ConsoleUi.ReadInt("Id bolesti");
                    var ill = db.Illnesses.Find(iid);
                    if (ill is null) { Console.WriteLine("Nije pronađena."); break; }
                    var d = ConsoleUi.ReadOptional("Nova dijagnoza");
                    if (d is not null) ill.Diagnosis = d;
                    ill.EndedOn = ConsoleUi.ReadOptionalDate("Kraj");
                    Console.WriteLine($"ChangeTracker prije SaveChanges: {db.ChangeTracker.Find(ill)?.State}");
                    db.ChangeTracker.DetectChanges();
                    Console.WriteLine($"Stanje: {db.ChangeTracker.Find(ill)?.State}, stupci: {string.Join(",", db.ChangeTracker.Find(ill)?.ModifiedColumns ?? [])}");
                    db.SaveChanges();
                    Console.WriteLine("Ažurirano.");
                    break;
                case 4:
                    var did = ConsoleUi.ReadInt("Id");
                    var del = db.Illnesses.Find(did);
                    if (del is null) break;
                    db.Illnesses.Remove(del);
                    db.SaveChanges();
                    Console.WriteLine("Obrisano.");
                    break;
            }
        }
    }

    public static void Medications(MedicalDbContext db)
    {
        while (true)
        {
            ConsoleUi.Title("Lijekovi");
            Console.WriteLine("1. Popis za bolest");
            Console.WriteLine("2. Dodaj");
            Console.WriteLine("3. Uredi");
            Console.WriteLine("4. Obriši");
            Console.WriteLine("0. Natrag");
            switch (ConsoleUi.ReadInt("Odabir"))
            {
                case 0: return;
                case 1:
                    var iid = ConsoleUi.ReadInt("Id bolesti");
                    foreach (var m in db.Medications.Where(x => x.IllnessId == iid).ToList())
                        Console.WriteLine("  " + m);
                    break;
                case 2:
                    db.Medications.Add(new Medication
                    {
                        IllnessId = ConsoleUi.ReadInt("Id bolesti"),
                        Name = ConsoleUi.ReadRequired("Naziv"),
                        Dose = ConsoleUi.ReadDecimal("Doza"),
                        Unit = ConsoleUi.ReadRequired("Jedinica (mg/tablete/jedinice)"),
                        Frequency = ConsoleUi.ReadRequired("Učestalost"),
                        IsCurrent = ConsoleUi.Confirm("Aktivan lijek") ? 1 : 0
                    });
                    db.SaveChanges();
                    Console.WriteLine("Spremljeno.");
                    break;
                case 3:
                    var mid = ConsoleUi.ReadInt("Id lijeka");
                    var med = db.Medications.Find(mid);
                    if (med is null) break;
                    var freq = ConsoleUi.ReadOptional("Nova učestalost");
                    if (freq is not null) med.Frequency = freq;
                    if (ConsoleUi.Confirm("Promijeni aktivnost"))
                        med.IsCurrent = med.IsCurrent == 1 ? 0 : 1;
                    db.SaveChanges();
                    Console.WriteLine("Ažurirano.");
                    break;
                case 4:
                    var del = db.Medications.Find(ConsoleUi.ReadInt("Id"));
                    if (del is null) break;
                    db.Medications.Remove(del);
                    db.SaveChanges();
                    Console.WriteLine("Obrisano.");
                    break;
            }
        }
    }

    public static void Examinations(MedicalDbContext db)
    {
        while (true)
        {
            ConsoleUi.Title("Specijalistički pregledi");
            Console.WriteLine("1. Popis (eager: pacijent + liječnik)");
            Console.WriteLine("2. Zakazi");
            Console.WriteLine("3. Uredi termin");
            Console.WriteLine("4. Otkaži (obriši)");
            Console.WriteLine("0. Natrag");
            switch (ConsoleUi.ReadInt("Odabir"))
            {
                case 0: return;
                case 1:
                    var list = db.Examinations
                        .OrderBy(e => e.ScheduledAt)
                        .Include(e => e.Patient)
                        .Include(e => e.Doctor)
                        .ToList();
                    foreach (var e in list)
                        Console.WriteLine($"  {e.Id}: {e.ExamType} {e.ScheduledAt:yyyy-MM-dd HH:mm} | {e.Patient?.LastName} | dr. {e.Doctor?.LastName}");
                    break;
                case 2:
                    Console.WriteLine("Dostupni liječnici:");
                    foreach (var d in db.Doctors.OrderBy(x => x.LastName).ToList())
                        Console.WriteLine("  " + d);
                    db.Examinations.Add(new Examination
                    {
                        PatientId = ConsoleUi.ReadInt("Id pacijenta"),
                        DoctorId = ConsoleUi.ReadInt("Id liječnika specijalista"),
                        ExamType = ConsoleUi.ReadExamType(),
                        ScheduledAt = ConsoleUi.ReadDateTimeOffset("Termin"),
                        Notes = ConsoleUi.ReadOptional("Napomena")
                    });
                    db.SaveChanges();
                    Console.WriteLine("Zakazano.");
                    break;
                case 3:
                    var ex = db.Examinations.Find(ConsoleUi.ReadInt("Id pregleda"));
                    if (ex is null) break;
                    ex.ScheduledAt = ConsoleUi.ReadDateTimeOffset("Novi termin");
                    db.SaveChanges();
                    Console.WriteLine("Ažurirano.");
                    break;
                case 4:
                    var del = db.Examinations.Find(ConsoleUi.ReadInt("Id"));
                    if (del is null) break;
                    db.Examinations.Remove(del);
                    db.SaveChanges();
                    Console.WriteLine("Otkazano.");
                    break;
            }
        }
    }

    public static void Addresses(MedicalDbContext db)
    {
        while (true)
        {
            ConsoleUi.Title("Adrese");
            Console.WriteLine("1. Popis");
            Console.WriteLine("2. Dodaj");
            Console.WriteLine("3. Uredi");
            Console.WriteLine("4. Obriši");
            Console.WriteLine("0. Natrag");
            switch (ConsoleUi.ReadInt("Odabir"))
            {
                case 0: return;
                case 1:
                    foreach (var a in db.Addresses.OrderBy(x => x.City).ToList())
                        Console.WriteLine($"  {a.Id}: {a.Street}, {a.PostalCode} {a.City}, {a.Country}");
                    break;
                case 2:
                    db.Addresses.Add(ReadAddress());
                    db.SaveChanges();
                    Console.WriteLine("Spremljeno.");
                    break;
                case 3:
                    var adr = db.Addresses.Find(ConsoleUi.ReadInt("Id"));
                    if (adr is null) break;
                    var street = ConsoleUi.ReadOptional("Nova ulica");
                    if (street is not null) adr.Street = street;
                    db.SaveChanges();
                    Console.WriteLine("Ažurirano.");
                    break;
                case 4:
                    var del = db.Addresses.Find(ConsoleUi.ReadInt("Id"));
                    if (del is null) break;
                    db.Addresses.Remove(del);
                    db.SaveChanges();
                    Console.WriteLine("Obrisano.");
                    break;
            }
        }
    }

    public static void DoctorsReadOnly(MedicalDbContext db)
    {
        ConsoleUi.Title("Liječnici (samo pregled — unos je bio moguć samo pri prvom pokretanju)");
        foreach (var d in db.Doctors.OrderBy(x => x.LastName).ToList())
            Console.WriteLine("  " + d);
    }

    public static void Demos(MedicalDbContext db)
    {
        ConsoleUi.Title("Demonstracija ORM značajki");
        var patient = db.Patients.OrderBy(p => p.Id).FirstOrDefault();
        if (patient is null)
        {
            Console.WriteLine("Nema pacijenata. Unesite podatke pa ponovite demo.");
            return;
        }

        Console.WriteLine("\n--- EAGER loading (Include) ---");
        var eager = db.Patients
            .Where(p => p.Id == patient.Id)
            .Include(p => p.Illnesses)
            .ThenInclude<Illness, IList<Medication>?>(i => i.Medications)
            .Include(p => p.ResidenceAddress)
            .FirstOrDefault();
        Console.WriteLine($"Pacijent {eager}: bolesti={eager?.Illnesses?.Count}, adresa={eager?.ResidenceAddress?.City}");
        Console.WriteLine("Eager dohvaća povezane retke unaprijed (manje round-tripova, veći SQL/JOIN ili IN upiti).");

        Console.WriteLine("\n--- LAZY loading (LazyLoadList) ---");
        var lazyPatient = db.Patients.Find(patient.Id);
        Console.WriteLine($"Kolekcija prije pristupa: {lazyPatient?.Illnesses?.GetType().Name}");
        var count = lazyPatient?.Illnesses?.Count ?? 0;
        Console.WriteLine($"Nakon .Count kolekcija se učitala: {count} bolesti.");
        Console.WriteLine("Lazy učitava tek pri prvom pristupu (N+1 opasnost ako se radi u petlji).");

        Console.WriteLine("\n--- Change tracking ---");
        var tracked = db.Patients.Find(patient.Id)!;
        var old = tracked.FirstName;
        tracked.FirstName = old + "*";
        db.ChangeTracker.DetectChanges();
        var entry = db.ChangeTracker.Find(tracked);
        Console.WriteLine($"Stanje={entry?.State}, izmijenjeni stupci={string.Join(",", entry?.ModifiedColumns ?? [])}");
        tracked.FirstName = old;
        db.ChangeTracker.DetectChanges();
        Console.WriteLine($"Nakon vraćanja: {db.ChangeTracker.Find(tracked)?.State}");
    }

    public static void Migrations(MedicalDbContext db)
    {
        ConsoleUi.Title("Migracije");
        Console.WriteLine("1. Prikaži predloženu migraciju (diff sheme i klasa)");
        Console.WriteLine("2. Izvrši pending migracije (UP)");
        Console.WriteLine("3. Rollback zadnje migracije (DOWN)");
        Console.WriteLine("4. Povijest");
        Console.WriteLine("0. Natrag");
        switch (ConsoleUi.ReadInt("Odabir"))
        {
            case 1:
                var gen = db.GenerateMigration();
                Console.WriteLine(gen.HasChanges ? gen.UpSql : "(nema razlike)");
                break;
            case 2:
                db.Migrate();
                Console.WriteLine("Gotovo.");
                break;
            case 3:
                try
                {
                    db.RollbackLastMigration();
                    Console.WriteLine("Rollback izvršen.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
                break;
            case 4:
                foreach (var h in new MiniOrm.Migrations.MigrationRunner(db).History())
                    Console.WriteLine($"  {h.Id}. {h.Name}  {h.AppliedAt:u}");
                break;
        }
    }

    public static void SeedDoctorsIfEmpty(MedicalDbContext db)
    {
        if (db.Doctors.Any())
            return;

        ConsoleUi.Title("Prvo pokretanje — unos liječnika");
        Console.WriteLine("Liječnike je moguće unijeti SAMO sada. Kasnije je popis zaključan.");
        if (ConsoleUi.Confirm("Učitati demo liječnike"))
        {
            db.Doctors.Add(new Doctor { FirstName = "Ana", LastName = "Kovač", Specialization = "Radiologija" });
            db.Doctors.Add(new Doctor { FirstName = "Marko", LastName = "Babić", Specialization = "Kardiologija" });
            db.Doctors.Add(new Doctor { FirstName = "Ivana", LastName = "Horvat", Specialization = "Oftalmologija" });
            db.Doctors.Add(new Doctor { FirstName = "Petar", LastName = "Jurić", Specialization = "Dermatologija" });
            db.Doctors.Add(new Doctor { FirstName = "Maja", LastName = "Perić", Specialization = "Dentalna medicina" });
            db.Doctors.Add(new Doctor { FirstName = "Luka", LastName = "Šimić", Specialization = "Neurologija" });
            db.SaveChanges();
            Console.WriteLine("Demo liječnici uneseni.");
            return;
        }

        var n = ConsoleUi.ReadInt("Koliko liječnika unosite");
        for (var i = 0; i < n; i++)
        {
            Console.WriteLine($"--- Liječnik {i + 1} ---");
            db.Doctors.Add(new Doctor
            {
                FirstName = ConsoleUi.ReadRequired("Ime"),
                LastName = ConsoleUi.ReadRequired("Prezime"),
                Specialization = ConsoleUi.ReadRequired("Specijalizacija")
            });
        }
        db.SaveChanges();
        Console.WriteLine("Liječnici spremljeni. Daljnji unos nije moguć kroz aplikaciju.");
    }

    public static void SeedDemoPatients(MedicalDbContext db)
    {
        if (db.Patients.Any())
        {
            Console.WriteLine("Pacijenti već postoje.");
            return;
        }

        var home = new Address { Street = "Ilica 12", City = "Zagreb", PostalCode = "10000" };
        var perm = new Address { Street = "Riva 8", City = "Split", PostalCode = "21000" };
        db.Addresses.Add(home);
        db.Addresses.Add(perm);
        db.SaveChanges();

        var patient = new Patient
        {
            FirstName = "Iva",
            LastName = "Marić",
            Oib = "12345678901",
            DateOfBirth = new DateTime(1992, 4, 15),
            Gender = Gender.Z,
            WeightKg = 62.5,
            ResidenceAddressId = home.Id,
            PermanentAddressId = perm.Id
        };
        db.Patients.Add(patient);
        db.SaveChanges();

        var illness = new Illness
        {
            PatientId = patient.Id,
            Diagnosis = "Hipertenzija",
            Notes = "Porodična anamneza.",
            StartedOn = new DateTime(2024, 1, 10)
        };
        db.Illnesses.Add(illness);
        db.SaveChanges();

        db.Medications.Add(new Medication
        {
            IllnessId = illness.Id,
            Name = "Lisinopril",
            Dose = 10,
            Unit = "mg",
            Frequency = "1x dnevno",
            IsCurrent = 1
        });

        var doc = db.Doctors.FirstOrDefault();
        if (doc is not null)
        {
            db.Examinations.Add(new Examination
            {
                PatientId = patient.Id,
                DoctorId = doc.Id,
                ExamType = ExamType.EKG,
                ScheduledAt = DateTimeOffset.Now.AddDays(7),
                Notes = "Kontrolni EKG"
            });
        }
        db.SaveChanges();
        Console.WriteLine("Demo pacijent, bolest, lijek i pregled uneseni.");
    }

    private static void CreatePatient(MedicalDbContext db)
    {
        Console.WriteLine("Adresa boravišta:");
        var res = ReadAddress();
        db.Addresses.Add(res);
        db.SaveChanges();

        int permId;
        if (ConsoleUi.Confirm("Prebivalište je isto kao boravište"))
        {
            permId = res.Id;
        }
        else
        {
            Console.WriteLine("Adresa prebivališta:");
            var perm = ReadAddress();
            db.Addresses.Add(perm);
            db.SaveChanges();
            permId = perm.Id;
        }

        db.Patients.Add(new Patient
        {
            FirstName = ConsoleUi.ReadRequired("Ime"),
            LastName = ConsoleUi.ReadRequired("Prezime"),
            Oib = ConsoleUi.ReadRequired("OIB (11 znakova)"),
            DateOfBirth = ConsoleUi.ReadDate("Datum rođenja"),
            Gender = ConsoleUi.ReadGender(),
            WeightKg = ConsoleUi.ReadOptionalDouble("Težina kg"),
            ResidenceAddressId = res.Id,
            PermanentAddressId = permId
        });
        db.SaveChanges();
        Console.WriteLine("Pacijent spremljen.");
    }

    private static void UpdatePatient(MedicalDbContext db)
    {
        var p = db.Patients.Find(ConsoleUi.ReadInt("Id"));
        if (p is null)
        {
            Console.WriteLine("Nije pronađen.");
            return;
        }

        var last = ConsoleUi.ReadOptional("Novo prezime");
        if (last is not null) p.LastName = last;
        var weight = ConsoleUi.ReadOptionalDouble("Nova težina kg");
        if (weight is not null) p.WeightKg = weight;

        db.ChangeTracker.DetectChanges();
        var entry = db.ChangeTracker.Find(p);
        Console.WriteLine($"ChangeTracker: {entry?.State}  [{string.Join(", ", entry?.ModifiedColumns ?? [])}]");
        db.SaveChanges();
        Console.WriteLine("Ažurirano (UPDATE je generiran samo za izmijenjene stupce).");
    }

    private static Address ReadAddress() => new()
    {
        Street = ConsoleUi.ReadRequired("Ulica i broj"),
        City = ConsoleUi.ReadRequired("Grad"),
        PostalCode = ConsoleUi.ReadOptional("Poštanski broj"),
        Country = ConsoleUi.ReadOptional("Država") ?? "Hrvatska"
    };

    private static void PrintPatient(Patient p)
    {
        Console.WriteLine(p);
        Console.WriteLine($"  rođenje={p.DateOfBirth:yyyy-MM-dd} spol={p.Gender} težina={p.WeightKg}");
        Console.WriteLine($"  boravište: {p.ResidenceAddress?.Street}, {p.ResidenceAddress?.City}");
        Console.WriteLine($"  prebivalište: {p.PermanentAddress?.Street}, {p.PermanentAddress?.City}");
        foreach (var i in p.Illnesses ?? Array.Empty<Illness>())
        {
            Console.WriteLine("  " + i);
            foreach (var m in i.Medications ?? Array.Empty<Medication>())
                Console.WriteLine("     " + m);
        }
        foreach (var e in p.Examinations ?? Array.Empty<Examination>())
            Console.WriteLine("  pregled " + e + (e.Doctor is null ? "" : $" dr.{e.Doctor.LastName}"));
    }
}
