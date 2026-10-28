using System.Security.Cryptography;

namespace HippoCrm.Core;

public sealed class AuthService(Database database)
{
    private const int Iterations = 600_000;
    public bool NeedsSetup
    {
        get { using var db = database.Open(); using var cmd = Database.Command(db, "SELECT count(*) FROM users"); return (long)cmd.ExecuteScalar()! == 0; }
    }
    public Session CreateAdministrator(string username, string password)
    {
        username = username.Trim();
        if (username.Length < 3 || username.Length > 64) throw new ArgumentException("Username must contain 3–64 characters.");
        if (password.Length < 12 || password.Length > 256) throw new ArgumentException("Use a password with 12–256 characters.");
        var salt = RandomNumberGenerator.GetBytes(32);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        using var db = database.Open();
        using var cmd = Database.Command(db, """
            INSERT INTO users(username,salt,password_hash,iterations,created_at)
            SELECT $name,$salt,$hash,$iterations,$now WHERE NOT EXISTS(SELECT 1 FROM users);
            """, ("$name",username),("$salt",salt),("$hash",hash),("$iterations",Iterations),("$now",Database.Now));
        if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("Administrator setup is already complete.");
        return SignIn(username,password);
    }
    public Session SignIn(string username, string password)
    {
        if (password.Length > 256) throw new ArgumentException("Invalid username or password.");
        using var db = database.Open();
        using var cmd = Database.Command(db,"SELECT id,username,salt,password_hash,iterations,failed_attempts,locked_until FROM users WHERE username=$name",("$name",username.Trim()));
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            _ = Rfc2898DeriveBytes.Pbkdf2(password,new byte[32],Iterations,HashAlgorithmName.SHA256,32);
            throw new ArgumentException("Invalid username or password.");
        }
        var id = reader.GetInt64(0); var name = reader.GetString(1);
        var salt = (byte[])reader[2]; var expected = (byte[])reader[3]; var iterations = reader.GetInt32(4);
        var attempts = reader.GetInt32(5);
        var locked = reader.IsDBNull(6) ? (DateTimeOffset?)null : DateTimeOffset.Parse(reader.GetString(6));
        reader.Close();
        if (locked > DateTimeOffset.UtcNow) throw new ArgumentException("Sign-in temporarily locked. Try again in 15 minutes.");
        if (locked.HasValue) attempts = 0;
        var actual = Rfc2898DeriveBytes.Pbkdf2(password,salt,iterations,HashAlgorithmName.SHA256,32);
        if (!CryptographicOperations.FixedTimeEquals(actual,expected))
        {
            attempts++;
            using var fail = Database.Command(db,"UPDATE users SET failed_attempts=$attempts,locked_until=$locked WHERE id=$id",
                ("$attempts",attempts),("$locked",attempts >= 5 ? DateTimeOffset.UtcNow.AddMinutes(15).ToString("O") : null),("$id",id));
            fail.ExecuteNonQuery();
            throw new ArgumentException("Invalid username or password.");
        }
        using var reset = Database.Command(db,"UPDATE users SET failed_attempts=0,locked_until=NULL WHERE id=$id",("$id",id));
        reset.ExecuteNonQuery();
        return new Session(id,name);
    }
}
