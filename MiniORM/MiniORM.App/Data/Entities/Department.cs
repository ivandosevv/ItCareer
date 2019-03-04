using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using MiniORM;

namespace MiniORM.App.Data.Entities
{
    public class Department
    {
        [Key]
        public int Id
        {
            get;
            set;
        }

        [Required]
        public string Name
        {
            get;
            set;
        }

        public ICollection<Employee> Employees
        {
            get;
        }
    }
}
