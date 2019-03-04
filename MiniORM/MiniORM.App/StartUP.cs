using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MiniORM.App
{
    using Data;
    using Data.Entities;

    class StartUp
    {
        static void Main(string[] args)
        {
            string connectionString = "Server=.\\SQLEXPRESS; " +
                "Database=MiniORM; " +
                "Integrated Security=True;";

            ItKarieraDbContext context = new ItKarieraDbContext(connectionString);

            context.Employees.Add(new Employee
            {
                FirstName = "Gosho",
                LastName = "Inserted",
                DepartmentID = context.Departments.First().Id,
                IsEmployed = true
            });

            Employee employee = context.Employees.Last();
            employee.FirstName = "Modified";

            context.Departments.Add(new Department
            {
                Name = "Personal Relations"
            });

            EmployeeProject firstProject = context.EmployeesProjects.First();
            context.EmployeesProjects.Remove(firstProject);

            context.SaveChanges();
        }
    }
}
