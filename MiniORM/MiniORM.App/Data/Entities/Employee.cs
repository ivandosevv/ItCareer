using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MiniORM.App.Data.Entities
{
    public class Employee
    {
        [Key]
        public int Id
        {
            get;
            set;
        }

        [Required]
        public string FirstName
        {
            get;
            set;
        }

        public string MiddleName
        {
            get;
            set;
        }

        [Required]
        public string LastName
        {
            get;
            set;
        }

        [Required]
        public bool IsEmployed
        {
            get;
            set;
        }

        [ForeignKey(nameof(Department))]
        public int DepartmentID
        {
            get;
            set;
        }

        public ICollection<EmployeeProject> EmployeeProjects
        {
            get;
        }
    }
}
