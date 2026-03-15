namespace Ringmaster.Core;

public static class SchemaVersionSupport
{
    public static int NormalizeForRead(string documentType, int schemaVersion)
    {
        if (schemaVersion <= 0)
        {
            return ProductInfo.SchemaVersion;
        }

        if (schemaVersion > ProductInfo.SchemaVersion)
        {
            throw new InvalidDataException(
                $"{documentType} schema version {schemaVersion} is not supported. Expected {ProductInfo.SchemaVersion} or earlier.");
        }

        return ProductInfo.SchemaVersion;
    }
}
