using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace MiniORM.App.Data
{
    using Entities;

    public class ItKarieraDbContext : DbContext
    {
        public ItKarieraDbContext(string connectionString)
            : base(connectionString)
        {

        }

        public DbSet<Employee> Employees
        {
            get;
        }

        public DbSet<Project> Projects
        {
            get;
        }

        public DbSet<Department> Departments
        {
            get;
        }

        public DbSet<EmployeeProject> EmployeesProjects
        {
            get;
        }
    }
}
