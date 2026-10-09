import { PlaceholderPage } from '../../components/placeholder-page';

export default function AdministrationPage() {
  return (
    <PlaceholderPage
      title="Administration"
      module="TenantService"
      description="Tenant and integration administration."
      relatedLinks={[
        { label: 'Tenants', href: '/administration/tenants' },
        { label: 'Integrations', href: '/administration/integrations' },
      ]}
    />
  );
}
