import { PersonListView } from '../../features/identity/components/person-list-view';

export default function PeoplePage() {
  return (
    <PersonListView
      title="People"
      description="Directory of every enrolled identity across People, Students, Staff, Alumni and Contractors."
      category="all"
      requiredPermission="nav:people"
      showEnrolmentCta
      showSuspendAction
    />
  );
}
