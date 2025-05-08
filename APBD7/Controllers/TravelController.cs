using APBD7.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace APBD7.Controllers;

[ApiController]
[Route("api")]
public class TravelController : ControllerBase
{
    private readonly string _connectionString = "Server=localhost;Database=APBD7;Integrated Security=true;TrustServerCertificate=true;";

    // Pobiera wszystkie dostępne wycieczki
    [HttpGet("trips")]
    public async Task<IActionResult> GetTripsAsync()
    {
        using SqlConnection con = new SqlConnection(_connectionString);
        await con.OpenAsync();

        using SqlCommand com = new SqlCommand("Select * from Trip", con);
        using SqlDataReader reader = await com.ExecuteReaderAsync();

        List<TripModel> trips = new List<TripModel>();
        while (await reader.ReadAsync())
        {
            trips.Add(new TripModel
            {
                IdTrip = (int)reader["IdTrip"],
                Name = (string)reader["Name"],
                Description = (string)reader["Description"],
                DateFrom = (DateTime)reader["DateFrom"],
                DateTo = (DateTime)reader["DateTo"],
                MaxPeople = (int)reader["MaxPeople"],
            });
        }
        return Ok(trips);
    }

    // Pobiera wszystkie wycieczki powiązane z klientem
    [HttpGet("clients/{id}/trips")]
    public async Task<IActionResult> GetClientTrips(int id)
    {
        using SqlConnection con = new SqlConnection(_connectionString);
        await con.OpenAsync();

        using SqlCommand comCheckIfExists = new SqlCommand("SELECT * FROM Client WHERE IdClient = @IdClient", con);
        comCheckIfExists.Parameters.AddWithValue("@IdClient", id);
        using SqlDataReader reader = await comCheckIfExists.ExecuteReaderAsync();

        if (!reader.HasRows)
            return NotFound("Nie ma takiego klienta");
        await reader.DisposeAsync();

        using SqlCommand com = new SqlCommand("SELECT * FROM CLIENT JOIN Client_Trip ON CLIENT.IdClient = Client_Trip.IdClient JOIN TRIP ON TRIP.IdTrip = Client_Trip.IdTrip WHERE Client.IdClient = @id", con);
        com.Parameters.AddWithValue("@id", id);
        using SqlDataReader tripReader = await com.ExecuteReaderAsync();

        List<object> list = new List<object>();
        while (await tripReader.ReadAsync())
        {
            list.Add(new
            {
                IdClient = (int)tripReader["IdTrip"],
                Description = (string)tripReader["Description"],
                RegisteredAt = (int)tripReader["RegisteredAt"],
                PaymentDate = tripReader["PaymentDate"]
            });
        }
        if (list.Count == 0) return Ok("Klient nie ma wycieczek");
        return Ok(list);
    }

    // Tworzy nowego klienta
    [HttpPost("clients")]
    public async Task<IActionResult> PostClient([FromBody] ClientModel model)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        using SqlConnection con = new SqlConnection(_connectionString);
        await con.OpenAsync();

        using SqlCommand comCheckIfExists = new SqlCommand("Select 1 from Client where pesel = @pesel", con);
        comCheckIfExists.Parameters.AddWithValue("@pesel", model.Pesel);
        using SqlDataReader reader = await comCheckIfExists.ExecuteReaderAsync();
        if (reader.HasRows)
            return Conflict("Klient o podanym PESELu już istnieje.");
        await reader.DisposeAsync();

        using SqlCommand com = new SqlCommand(@"
            INSERT INTO Client (FirstName, LastName, Email, Telephone, Pesel)
            OUTPUT INSERTED.IdClient
            VALUES (@FirstName, @LastName, @Email, @Telephone, @Pesel)", con);
        com.Parameters.AddWithValue("@FirstName", model.FirstName);
        com.Parameters.AddWithValue("@LastName", model.LastName);
        com.Parameters.AddWithValue("@Email", model.Email);
        com.Parameters.AddWithValue("@Telephone", model.Telephone);
        com.Parameters.AddWithValue("@Pesel", model.Pesel);

        var insertedId = (int)await com.ExecuteScalarAsync();
        return Ok("dodano klienta o id "+insertedId);
    }

    // Rejestruje klienta na wycieczkę
    [HttpPut("clients/{id}/trips/{tripId}")]
    public async Task<IActionResult> Trip(int id, int tripId)
    {
        using SqlConnection con = new SqlConnection(_connectionString);
        await con.OpenAsync();

        using SqlCommand checkIfExists = new SqlCommand(@"
            SELECT 1 FROM Client
            JOIN Trip ON 1=1
            WHERE IdClient = @IdClient AND IdTrip = @IdTrip", con);
        checkIfExists.Parameters.AddWithValue("@IdClient", id);
        checkIfExists.Parameters.AddWithValue("@IdTrip", tripId);
        var exists = await checkIfExists.ExecuteScalarAsync();
        if (exists == null)
            return NotFound("Nie ma takiego klienta lub wycieczki.");

        using SqlCommand checkIfAlreadyAdded = new SqlCommand(@"
            SELECT 1 FROM Client_Trip
            WHERE IdClient = @IdClient AND IdTrip = @IdTrip", con);
        checkIfAlreadyAdded.Parameters.AddWithValue("@IdClient", id);
        checkIfAlreadyAdded.Parameters.AddWithValue("@IdTrip", tripId);
        var alreadyAdded = await checkIfAlreadyAdded.ExecuteScalarAsync();
        if (alreadyAdded != null)
            return Conflict("Klient już zapisany na tę wycieczkę.");

        using SqlCommand getLimits = new SqlCommand(@"
            SELECT 
                (SELECT MaxPeople FROM Trip WHERE IdTrip = @IdTrip) AS MaxPeople,
                (SELECT COUNT(*) FROM Client_Trip WHERE IdTrip = @IdTrip) AS CurrentCount", con);
        getLimits.Parameters.AddWithValue("@IdTrip", tripId);

        using SqlDataReader reader = await getLimits.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            await reader.DisposeAsync();
            return NotFound("Wycieczka nie istnieje.");
        }

        int max = (int)reader["MaxPeople"];
        int current = (int)reader["CurrentCount"];
        await reader.DisposeAsync();

        if (current >= max)
            return BadRequest("Osiągnięto maksymalną liczbę uczestników.");

        int registeredAt = int.Parse(DateTime.Now.ToString("yyyyMMdd"));

        using SqlCommand insert = new SqlCommand(@"
            INSERT INTO Client_Trip (IdClient, IdTrip, RegisteredAt)
            VALUES (@IdClient, @IdTrip, @RegisteredAt)", con);
        insert.Parameters.AddWithValue("@IdClient", id);
        insert.Parameters.AddWithValue("@IdTrip", tripId);
        insert.Parameters.AddWithValue("@RegisteredAt", registeredAt);

        await insert.ExecuteNonQueryAsync();

        return Ok("Klient został przypisany do wycieczki");
    }

    // Usuwa klienta z wycieczki
    [HttpDelete("clients/{id}/trips/{tripId}")]
    public async Task<IActionResult> DeleteClientTrip(int id, int tripId)
    {
        using SqlConnection con = new SqlConnection(_connectionString);
        await con.OpenAsync();

        using SqlCommand checkIfExists = new SqlCommand(@"
            SELECT 1 FROM Client_Trip
            WHERE IdClient = @IdClient AND IdTrip = @IdTrip", con);
        checkIfExists.Parameters.AddWithValue("@IdClient", id);
        checkIfExists.Parameters.AddWithValue("@IdTrip", tripId);
        var exists = await checkIfExists.ExecuteScalarAsync();
        if (exists == null)
            return NotFound("Rejestracja klienta na tę wycieczkę nie istnieje.");

        using SqlCommand delete = new SqlCommand(@"
            DELETE FROM Client_Trip
            WHERE IdClient = @IdClient AND IdTrip = @IdTrip", con);
        delete.Parameters.AddWithValue("@IdClient", id);
        delete.Parameters.AddWithValue("@IdTrip", tripId);

        await delete.ExecuteNonQueryAsync();

        return Ok("Rejestracja klienta na wycieczkę została usunięta.");
    }
}
