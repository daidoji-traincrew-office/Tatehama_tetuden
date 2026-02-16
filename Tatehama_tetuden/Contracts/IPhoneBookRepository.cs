using Tatehama_tetuden.Models;

namespace Tatehama_tetuden.Contracts;

public interface IPhoneBookRepository
{
    List<PhoneBookEntry> GetAll();
    Task<List<PhoneBookEntry>> GetAllStationsAsync();
    PhoneBookEntry? FindByNumber(string number);
}
