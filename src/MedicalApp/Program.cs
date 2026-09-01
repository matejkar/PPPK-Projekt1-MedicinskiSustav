using MedicalApp;

var connectionString =
    Environment.GetEnvironmentVariable("MEDICAL_DB")
    ?? "Host=localhost;Port=5433;Database=medical_db;Username=medic;Password=medic";

Console.OutputEncoding = System.Text.Encoding.UTF8;

using var db = new MedicalDbContext(connectionString);

try
{
    db.Migrate();
}
catch (Exception ex)
{
    Console.WriteLine("Ne mogu se povezati na Postgres / izvršiti migracije.");
    Console.WriteLine("Pokrenite: docker compose up -d  (u mapi Projekt1-MedicinskiSustav)");
    Console.WriteLine(ex.Message);
    return;
}

Menus.SeedDoctorsIfEmpty(db);

while (true)
{
    ConsoleUi.Title("Medicinski sustav  |  MiniOrm + PostgreSQL");
    Console.WriteLine("1. Pacijenti");
    Console.WriteLine("2. Povijest bolesti");
    Console.WriteLine("3. Lijekovi");
    Console.WriteLine("4. Specijalistički pregledi");
    Console.WriteLine("5. Adrese");
    Console.WriteLine("6. Liječnici (pregled)");
    Console.WriteLine("7. Demo: eager / lazy / change tracking");
    Console.WriteLine("8. Migracije");
    Console.WriteLine("9. Učitaj demo pacijenta");
    Console.WriteLine("0. Izlaz");

    switch (ConsoleUi.ReadInt("Odabir"))
    {
        case 0:
            return;
        case 1:
            Menus.Patients(db);
            break;
        case 2:
            Menus.Illnesses(db);
            break;
        case 3:
            Menus.Medications(db);
            break;
        case 4:
            Menus.Examinations(db);
            break;
        case 5:
            Menus.Addresses(db);
            break;
        case 6:
            Menus.DoctorsReadOnly(db);
            break;
        case 7:
            Menus.Demos(db);
            break;
        case 8:
            Menus.Migrations(db);
            break;
        case 9:
            Menus.SeedDemoPatients(db);
            break;
    }
}
