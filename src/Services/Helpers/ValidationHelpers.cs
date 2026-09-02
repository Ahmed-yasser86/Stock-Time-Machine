using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Servicess.Helpers
{
    public class ValidationHelpers
    {

        internal static void ValidationFunction(object obj)
        {
            // 1. Guard against null input to prevent crash on the next line
            if (obj == null) throw new ArgumentNullException(nameof(obj));

            ValidationContext validationContext = new ValidationContext(obj);
            List<ValidationResult> validationResults = new List<ValidationResult>();

            // The 'true' here ensures all properties are validated, not just [Required]
            bool isValid = Validator.TryValidateObject(obj, validationContext, validationResults, true);

            if (!isValid)
            {
                // Concatenate all errors into one message
                string errorMessages = validationResults.FirstOrDefault()?.ErrorMessage;

                // Suggestion: Throw ArgumentException so it's clear the input was the problem
                throw new ArgumentException(errorMessages);
            }
        }

    }
}
