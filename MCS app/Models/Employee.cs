namespace MCS_app.Models
{
    public class Employee
    {
        public int Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string JobTitle { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public decimal Salary { get; set; }
        public DateOnly HireDate { get; set; }
    }
}
////http://172.20.10.3:40843/swagger
////http://172.20.10.3:40843/api/employees
//http://172.20.10.3:40843/api/employees/{id}